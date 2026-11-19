using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SpeedBudget.Core;

namespace SpeedBudget.Data;

/// <summary>One usable line of a transactions file. Amount is signed: money in is positive, money out is negative.</summary>
public sealed record TransactionImportRow(int Line, DateOnly Date, string Payee, string? Memo, string? ItemName, string? AccountName, long Amount, bool Cleared, string? MonthLabel = null, bool Undated = false)
{
    /// <summary>A "New Month" line rather than a transaction: it starts the month named by <see cref="MonthLabel"/>.</summary>
    public bool IsMonth => MonthLabel is not null;
}

public sealed record TransactionImportFile(IReadOnlyList<TransactionImportRow> Rows, IReadOnlyList<ImportProblem> Problems);

/// <summary>What an import of transactions did (or would do): how many were added, the lines left out and why, and notes such as budget items that were not found.</summary>
public sealed record TransactionImportResult(int Added, IReadOnlyList<ImportProblem> Skipped, IReadOnlyList<string> Notes, int MonthLines = 0);

/// <summary>
/// Reads an Excel file of transactions (an export from the app, or a sheet of your own), and exports a table of transactions.
/// The columns are Date, Counterparty, Memo, Budget item, Account (only when importing into all accounts), Outflow, Inflow and Cleared.
/// </summary>
public static class TransactionImport
{
    private const string SheetName = "Transactions";

    /// <summary>The Counterparty of a line that starts a month (its Memo names the month).</summary>
    public const string MonthRowName = "New Month";

    /// <summary>Reads a sheet with the headings below. Lines that cannot be used are reported, not imported.</summary>
    public static TransactionImportFile Read(string path, bool withAccount)
    {
        var rows = new List<TransactionImportRow>();
        var problems = new List<ImportProblem>();

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);   // a file still open in Excel can be read
        using var book = new XLWorkbook(stream);
        var sheet = book.Worksheets.FirstOrDefault(s => s.Name.Equals(SheetName, StringComparison.OrdinalIgnoreCase)) ?? book.Worksheets.First();

        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var headingRow = 0;
        var columns = new Dictionary<string, int>();
        for (var r = 1; r <= Math.Min(last, 20) && headingRow == 0; r++)
        {
            var found = new Dictionary<string, int>();
            foreach (var cell in sheet.Row(r).CellsUsed())
            {
                var name = cell.GetString().Trim().ToLowerInvariant();
                if (name.StartsWith("date")) found.TryAdd("date", cell.Address.ColumnNumber);
                else if (name.StartsWith("counterparty") || name.StartsWith("payee")) found.TryAdd("payee", cell.Address.ColumnNumber);
                else if (name.StartsWith("memo")) found.TryAdd("memo", cell.Address.ColumnNumber);
                else if (name.StartsWith("budget item") || name == "item") found.TryAdd("item", cell.Address.ColumnNumber);
                else if (name.StartsWith("account")) found.TryAdd("account", cell.Address.ColumnNumber);
                else if (name.StartsWith("outflow")) found.TryAdd("outflow", cell.Address.ColumnNumber);
                else if (name.StartsWith("inflow")) found.TryAdd("inflow", cell.Address.ColumnNumber);
                else if (name.StartsWith("cleared")) found.TryAdd("cleared", cell.Address.ColumnNumber);
            }
            if (found.ContainsKey("date") && found.ContainsKey("payee") && (found.ContainsKey("outflow") || found.ContainsKey("inflow"))) { headingRow = r; columns = found; }
        }
        if (headingRow == 0)
        {
            problems.Add(new ImportProblem(1, "The Date, Counterparty and Outflow or Inflow headings were not found. Put the headings in the first row (an export from Speed Budget has them)."));
            return new TransactionImportFile(rows, problems);
        }
        if (withAccount && !columns.ContainsKey("account"))
        {
            problems.Add(new ImportProblem(1, "The Account heading was not found. For all accounts, use a sheet with an Account column, such as an export from the all-accounts table."));
            return new TransactionImportFile(rows, problems);
        }

        string Text(IXLRow row, string key) => columns.TryGetValue(key, out var c) ? row.Cell(c).GetString().Trim() : "";

        for (var r = headingRow + 1; r <= last; r++)
        {
            var row = sheet.Row(r);
            var dateCell = row.Cell(columns["date"]);
            var outCell = columns.TryGetValue("outflow", out var oc) ? row.Cell(oc) : null;
            var inCell = columns.TryGetValue("inflow", out var ic) ? row.Cell(ic) : null;
            var payee = Text(row, "payee");

            if (dateCell.IsEmpty() && payee == "" && (outCell?.IsEmpty() ?? true) && (inCell?.IsEmpty() ?? true)) continue;   // blank space

            // A "New Month" line: the counterparty says so and the memo names the month.
            if (payee.Equals(MonthRowName, StringComparison.OrdinalIgnoreCase))
            {
                var monthText = Text(row, "memo");
                if (!SpeedBudget.Core.MonthLabels.TryParse(monthText, out var monthStart)) { problems.Add(new ImportProblem(r, $"New Month: the month \"{monthText}\" could not be read. Write it like December 2026 in the Memo column.")); continue; }
                rows.Add(new TransactionImportRow(r, default, MonthRowName, null, null, null, 0, false, SpeedBudget.Core.MonthLabels.For(monthStart)));
                continue;
            }

            // A row with no date at all is an expected transaction, with no date yet.
            var undated = dateCell is null || dateCell.IsEmpty();
            DateOnly date = TransactionEntity.UndatedPlace;
            if (!undated && !BalanceImport.TryDate(dateCell!, out date)) { problems.Add(new ImportProblem(r, "The date could not be read.")); continue; }
            var label = undated ? "No date" : $"{date:d MMM yyyy}";
            // (a blank counterparty is allowed: the bank file import can leave it blank too)

            long outflow = 0, inflow = 0;
            if (outCell is not null && !outCell.IsEmpty())
            {
                if (!BalanceImport.TryMoney(outCell, out outflow)) { problems.Add(new ImportProblem(r, $"{label}: the outflow could not be read as an amount.")); continue; }
                outflow = Math.Abs(outflow);
            }
            if (inCell is not null && !inCell.IsEmpty())
            {
                if (!BalanceImport.TryMoney(inCell, out inflow)) { problems.Add(new ImportProblem(r, $"{label}: the inflow could not be read as an amount.")); continue; }
                inflow = Math.Abs(inflow);
            }
            if (outflow == 0 && inflow == 0) { problems.Add(new ImportProblem(r, $"{label}: there is no amount.")); continue; }

            var account = Text(row, "account");
            if (withAccount && account == "") { problems.Add(new ImportProblem(r, $"{label}: there is no account.")); continue; }

            var memo = Text(row, "memo");
            var item = Text(row, "item");
            var cleared = Text(row, "cleared").ToLowerInvariant() is "yes" or "y" or "true" or "1" or "x" or "cleared";
            rows.Add(new TransactionImportRow(r, date, payee, memo == "" ? null : memo, item == "" ? null : item, withAccount ? account : null, inflow - outflow, cleared, null, undated));
        }
        return new TransactionImportFile(rows, problems);
    }
}

public sealed partial class BudgetStore
{
    /// <summary>
    /// Brings transactions read from an Excel file in, all in one save (so Ctrl+Z takes the whole import back). Into one account when
    /// <paramref name="accountId"/> is given; otherwise each line names its account. An account or budget item is found by its name.
    /// A line whose account is not found is skipped; a budget item that is not found is left empty. With <paramref name="apply"/> false nothing is saved.
    /// </summary>
    public async Task<TransactionImportResult> ImportTransactionsAsync(Guid? accountId, IEnumerable<TransactionImportRow> rows, bool apply = true, CancellationToken ct = default)
    {
        var accounts = await db.Accounts.Where(a => !a.IsClosed).ToListAsync(ct);
        var items = await db.Items.ToListAsync(ct);
        var snapshot = await LoadSnapshotAsync(ct);
        var firstLabel = BudgetEngine.FirstMonthLabelFor(snapshot);
        var labelsInUse = snapshot.Markers.Select(m => m.Label).Append(firstLabel).ToHashSet();
        var skipped = new List<ImportProblem>();
        var notes = new List<string>();
        var missingItems = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextOrder = new Dictionary<DateOnly, int>();
        var payees = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toAdd = new List<(TransactionEntity Entity, Guid? ItemId)>();
        // Transactions already in the table are not added again (so importing a file that was exported from the table changes nothing). A line is
        // taken to be one that is there when the account, date, counterparty, memo and amount are all the same; each existing transaction is used up once.
        static string Same(Guid account, DateOnly date, long amount, string payee, string? memo) =>
            $"{account}|{date:yyyy-MM-dd}|{amount}|{payee.Trim().ToLowerInvariant()}|{(memo ?? "").Trim().ToLowerInvariant()}";
        var present = new Dictionary<string, int>();
        foreach (var t in await db.Transactions.AsNoTracking().Select(t => new { t.AccountId, t.Date, t.Amount, t.Payee, t.Memo, t.TransferPairId }).ToListAsync(ct))
        {
            var key = Same(t.AccountId, t.Date, t.Amount, t.TransferPairId is null ? t.Payee : $"Transfer: {t.Payee}", t.Memo);   // a transfer is exported as "Transfer: other account"
            present[key] = present.GetValueOrDefault(key) + 1;
        }
        var duplicates = 0;
        var months = new List<(string Label, DateOnly Month, int Before)>();   // Before: how many transactions come ahead of the line in the file

        foreach (var row in rows)
        {
            if (row.IsMonth)
            {
                MonthLabels.TryParse(row.MonthLabel, out var month);
                if (MonthLabels.TryParse(firstLabel, out var first) && month < first) { skipped.Add(new ImportProblem(row.Line, $"New Month {row.MonthLabel}: the budget starts in {firstLabel}, so a line for an earlier month can't be added.")); continue; }
                if (!labelsInUse.Add(row.MonthLabel!)) { notes.Add($"{row.MonthLabel} already has its line, so it was not added again."); continue; }
                months.Add((row.MonthLabel!, month, toAdd.Count));
                continue;
            }

            var account = accountId is { } id
                ? accounts.FirstOrDefault(a => a.Id == id)
                : accounts.FirstOrDefault(a => string.Equals(a.Name, row.AccountName, StringComparison.OrdinalIgnoreCase));
            if (account is null) { skipped.Add(new ImportProblem(row.Line, $"{row.Date:d MMM yyyy}: the account {(accountId is null ? $"\"{row.AccountName}\" " : "")}was not found.")); continue; }
            if (account.TrackBalances) { skipped.Add(new ImportProblem(row.Line, $"{row.Date:d MMM yyyy}: {account.Name} is kept by its balances, not by transactions.")); continue; }

            var seen = Same(account.Id, row.Date, row.Amount, row.Payee, row.Memo);
            if (present.TryGetValue(seen, out var left) && left > 0) { present[seen] = left - 1; duplicates++; continue; }

            Guid? itemId = null;
            if (row.ItemName is { } itemName && account.IsOnBudget)
            {
                var item = items.FirstOrDefault(i => string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase) || string.Equals(i.DisplayName, itemName, StringComparison.OrdinalIgnoreCase));
                if (item is null) missingItems.Add(itemName); else itemId = item.Id;
            }

            // Orders go up in twos, which leaves room for a month line between two transactions.
            if (!nextOrder.TryGetValue(row.Date, out var order)) order = await NextDayOrderAsync(row.Date, ct);
            nextOrder[row.Date] = order + 2;
            toAdd.Add((new TransactionEntity { AccountId = account.Id, Date = row.Date, DayOrder = order, Payee = row.Payee, Memo = row.Memo, Amount = row.Amount, IsCleared = row.Cleared, IsUndated = row.Undated }, itemId));
            if (row.Payee != "") payees.Add(row.Payee);
        }
        if (duplicates > 0) notes.Add(duplicates == 1 ? "1 transaction was already in the table, so it was not added again." : $"{duplicates} transactions were already in the table, so they were not added again.");
        if (missingItems.Count > 0) notes.Add($"Budget items not found, so those transactions have no item: {string.Join(", ", missingItems)}.");

        var result = new TransactionImportResult(toAdd.Count, skipped, notes, months.Count);
        if (!apply || (toAdd.Count == 0 && months.Count == 0)) return result;

        foreach (var payee in payees) await EnsurePayeeAsync(payee, ct);   // new counterparties join the list (a step of their own)
        foreach (var (entity, itemId) in toAdd)
        {
            db.Transactions.Add(entity);
            db.SplitLines.Add(new SplitLineEntity { TransactionId = entity.Id, BudgetItemId = itemId, Amount = entity.Amount });
        }

        // A month line goes just ahead of the transaction that follows it in the file (or just after the last one).
        var loose = new List<DateOnly>();
        foreach (var (label, month, before) in months)
        {
            if (toAdd.Count == 0) { loose.Add(month); continue; }
            var (date, order) = before < toAdd.Count ? (toAdd[before].Entity.Date, toAdd[before].Entity.DayOrder - 1) : (toAdd[^1].Entity.Date, toAdd[^1].Entity.DayOrder + 1);
            var marker = new MonthMarkerEntity { Date = date, DayOrder = order, Label = label };
            await MoveBudgetAsync(BudgetEngine.VirtualMonthId(month), marker.Id, ct);   // a month already planned without a line moves its budget to the new line
            db.Markers.Add(marker);
        }
        await db.SaveChangesAsync(ct);
        foreach (var month in loose) await AddMonthAsync(month, ct);   // a file with month lines only: they go where a new month normally starts
        return result;
    }

    /// <summary>Deletes several transactions in one save, so Ctrl+Z brings them all back together. Deleting one side of a transfer deletes the other side too.</summary>
    public async Task DeleteTransactionsAsync(IEnumerable<TransactionEntity> transactions, CancellationToken ct = default)
    {
        var all = transactions.ToList();
        var pairs = all.Where(t => t.TransferPairId is not null).Select(t => t.TransferPairId!.Value).Distinct().ToList();
        var singles = all.Where(t => t.TransferPairId is null).ToList();
        if (pairs.Count > 0) db.Transactions.RemoveRange(await db.Transactions.Where(t => t.TransferPairId != null && pairs.Contains(t.TransferPairId.Value)).ToListAsync(ct));
        db.Transactions.RemoveRange(singles);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Writes the transactions of one account (or of every account) to an Excel file, oldest first.</summary>
    public async Task ExportTransactionsAsync(string path, Guid? accountId, CancellationToken ct = default)
    {
        var accounts = (await db.Accounts.ToListAsync(ct)).ToDictionary(a => a.Id);
        var items = (await db.Items.ToListAsync(ct)).ToDictionary(i => i.Id);
        var transactions = await GetTransactionsAsync(accountId, ct);

        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Transactions");
        string[] headings = ["Date", "Bank date", "Counterparty", "Memo", "Budget item", "Account", "Outflow", "Inflow", "Cleared"];
        for (var i = 0; i < headings.Length; i++) sheet.Cell(1, i + 1).Value = headings[i];
        var header = sheet.Range(1, 1, 1, headings.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E7F5");
        sheet.SheetView.FreezeRows(1);

        var r = 2;
        // The month lines go in among the transactions where they stand in the table: the first month at the top, the others by their place.
        void MonthRow(string label)
        {
            sheet.Cell(r, 3).Value = TransactionImport.MonthRowName;
            sheet.Cell(r, 4).Value = label;
            var row = sheet.Range(r, 1, r, headings.Length);
            row.Style.Font.Bold = true;
            row.Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE4D6");
            r++;
        }
        MonthRow(await GetFirstMonthLabelAsync(ct));
        var markers = (await GetMarkersAsync(ct)).OrderBy(m => m.Date).ThenBy(m => m.DayOrder).ToList();
        var nextMarker = 0;
        foreach (var (t, lines) in transactions)
        {
            while (nextMarker < markers.Count && (markers[nextMarker].Date, markers[nextMarker].DayOrder).CompareTo((t.Date, t.DayOrder)) <= 0)
                MonthRow(markers[nextMarker++].Label);
            if (!t.IsUndated) sheet.Cell(r, 1).Value = t.Date.ToDateTime(TimeOnly.MinValue);   // an expected transaction with no date has an empty Date
            if (t.BankDate is { } bank) sheet.Cell(r, 2).Value = bank.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(r, 3).Value = t.TransferPairId is null ? t.Payee : $"Transfer: {t.Payee}";
            sheet.Cell(r, 4).Value = t.Memo ?? "";
            var names = lines.Where(l => l.BudgetItemId is not null && items.ContainsKey(l.BudgetItemId.Value)).Select(l => items[l.BudgetItemId!.Value].Name).Distinct().ToList();
            sheet.Cell(r, 5).Value = lines.Count > 1 ? $"Split: {string.Join(", ", names)}" : string.Join(", ", names);
            sheet.Cell(r, 6).Value = accounts.TryGetValue(t.AccountId, out var account) ? account.Name : "";
            if (t.Amount < 0) sheet.Cell(r, 7).Value = (double)(-t.Amount) / 100;
            if (t.Amount > 0) sheet.Cell(r, 8).Value = (double)t.Amount / 100;
            sheet.Cell(r, 9).Value = t.IsCleared ? "Yes" : "";
            r++;
        }
        while (nextMarker < markers.Count) MonthRow(markers[nextMarker++].Label);
        sheet.Range(2, 1, Math.Max(r - 1, 2), 2).Style.DateFormat.Format = "yyyy-mm-dd";
        sheet.Range(2, 7, Math.Max(r - 1, 2), 8).Style.NumberFormat.Format = "#,##0.00";
        for (var i = 0; i < headings.Length; i++) sheet.Column(i + 1).AdjustToContents(1, Math.Min(r, 200));
        book.SaveAs(path);
    }

    /// <summary>Writes the lines of an account kept by its balances to an Excel file, oldest first, with the changes, gains and losses worked out. The first four columns can be imported again.</summary>
    public async Task ExportBalancesAsync(string path, Guid accountId, CancellationToken ct = default)
    {
        var report = await GetBalanceReportAsync(accountId, ct);
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Balances");
        string[] headings = ["Date", "Balance", "Withdrawal", "Comment", "Total changes", "Daily changes", "Gains / losses"];
        for (var i = 0; i < headings.Length; i++) sheet.Cell(1, i + 1).Value = headings[i];
        var header = sheet.Range(1, 1, 1, headings.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E7F5");
        sheet.SheetView.FreezeRows(1);

        var r = 2;
        foreach (var row in report.Rows)
        {
            sheet.Cell(r, 1).Value = row.Date.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(r, 2).Value = (double)row.Balance / 100;
            if (row.Withdrawal != 0) sheet.Cell(r, 3).Value = (double)row.Withdrawal / 100;
            sheet.Cell(r, 4).Value = row.Comment ?? "";
            sheet.Cell(r, 5).Value = (double)row.TotalChange / 100;
            sheet.Cell(r, 6).Value = (double)row.DailyChange / 100;
            sheet.Cell(r, 7).Value = (double)row.GainLoss / 100;
            r++;
        }
        sheet.Range(2, 1, Math.Max(r - 1, 2), 1).Style.DateFormat.Format = "yyyy-mm-dd";
        sheet.Range(2, 2, Math.Max(r - 1, 2), 3).Style.NumberFormat.Format = "#,##0.00";
        sheet.Range(2, 5, Math.Max(r - 1, 2), 7).Style.NumberFormat.Format = "#,##0.00";
        for (var i = 0; i < headings.Length; i++) sheet.Column(i + 1).AdjustToContents(1, Math.Min(r, 200));
        book.SaveAs(path);
    }
}
