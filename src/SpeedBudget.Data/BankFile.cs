using System.Globalization;
using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SpeedBudget.Core;

namespace SpeedBudget.Data;

/// <summary>One line of a bank file. Amount is signed in cents: money in is positive, money out is negative.</summary>
public sealed record BankLine(int Line, DateOnly Date, string Description, string? Memo, long Amount);

/// <summary>
/// Where the lines of a bank file are: how many heading rows come first, and which column holds what (-1 = none). Money is either
/// one Amount column or separate Debit and Credit columns. <paramref name="NegativeIsOut"/> says whether a negative amount is money out
/// (most banks) or money in.
/// </summary>
public sealed record BankFormat(int HeaderRows, int DateColumn, string DateFormat, int DescriptionColumn, int AmountColumn, int DebitColumn, int CreditColumn, bool NegativeIsOut, int MemoColumn = -1);

/// <summary>A file's table of cells, as text. The file name is kept for zip files that hold more than one.</summary>
public sealed record BankTable(string Name, IReadOnlyList<string[]> Rows)
{
    public int Columns => Rows.Count == 0 ? 0 : Rows.Max(r => r.Length);
    public string Cell(int row, int column) => row >= 0 && row < Rows.Count && column >= 0 && column < Rows[row].Length ? Rows[row][column] : "";
}

public sealed record BankParse(IReadOnlyList<BankLine> Lines, IReadOnlyList<ImportProblem> Problems);

/// <summary>
/// Reads transactions from a bank's file: Excel, CSV (or tab or semicolon separated text), or a zip holding any of those. The layout (which
/// column is the date, the description, the money) is guessed from the first lines, and can be corrected by the user.
/// </summary>
public static class BankFile
{
    public static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd", "dd/MM/yyyy", "dd-MM-yyyy", "dd.MM.yyyy", "dd MMM yyyy", "d MMM yyyy", "dd/MM/yy", "dd-MMM-yy", "dd-MMM-yyyy",
        "MM/dd/yyyy", "MM-dd-yyyy", "MM/dd/yy", "MMM d, yyyy", "MMMM d, yyyy", "d MMMM yyyy",
    ];

    /// <summary>The file types that can be picked.</summary>
    public static readonly string[] Extensions = [".xlsx", ".xlsm", ".csv", ".txt", ".tsv", ".zip"];

    // ---- Reading a file into tables ----

    /// <summary>The first table of a file.</summary>
    public static BankTable ReadTable(string path) => ReadTables(path).FirstOrDefault() ?? new BankTable(Path.GetFileName(path), []);

    /// <summary>Every table in a file: one for a sheet of cells or a CSV, one for each Excel sheet that has cells, and those of each file in a zip.</summary>
    public static IReadOnlyList<BankTable> ReadTables(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);   // a file still open in Excel can be read
        var tables = new List<BankTable>();
        ReadStream(stream, Path.GetFileName(path), tables, 0);
        return tables.Where(t => t.Rows.Count > 0).ToList();
    }

    private static void ReadStream(Stream stream, string name, List<BankTable> tables, int depth)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext == ".zip")
        {
            if (depth > 1) return;
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries.Where(e => e.Length > 0 && !e.FullName.StartsWith("__MACOSX", StringComparison.Ordinal) && !e.Name.StartsWith('.')).OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
            {
                if (!Extensions.Contains(Path.GetExtension(entry.Name).ToLowerInvariant())) continue;
                using var inner = new MemoryStream();
                using (var s = entry.Open()) s.CopyTo(inner);
                inner.Position = 0;
                ReadStream(inner, entry.FullName, tables, depth + 1);
            }
        }
        else if (ext is ".xlsx" or ".xlsm")
        {
            using var book = new XLWorkbook(stream);
            foreach (var sheet in book.Worksheets)
            {
                var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
                var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
                if (last == 0 || lastColumn == 0) continue;
                var rows = new List<string[]>();
                for (var r = 1; r <= last; r++)
                {
                    var cells = new string[lastColumn];
                    for (var c = 1; c <= lastColumn; c++) cells[c - 1] = CellText(sheet.Cell(r, c));
                    rows.Add(cells);
                }
                tables.Add(new BankTable(book.Worksheets.Count > 1 ? $"{name} · {sheet.Name}" : name, rows));
            }
        }
        else
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            tables.Add(new BankTable(name, ReadDelimited(reader.ReadToEnd())));
        }
    }

    private static string CellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return "";
        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var date)) return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (cell.DataType == XLDataType.Number && cell.TryGetValue<double>(out var number)) return number.ToString("0.########", CultureInfo.InvariantCulture);
        return cell.GetString().Trim();
    }

    /// <summary>Splits text into rows and cells, with the separator (comma, semicolon or tab) guessed from the first lines and quotes honoured.</summary>
    public static IReadOnlyList<string[]> ReadDelimited(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var sample = string.Join('\n', text.Split('\n').Take(10));
        var separator = new[] { ',', ';', '\t', '|' }.OrderByDescending(c => sample.Count(x => x == c)).First();
        if (sample.Count(c => c == separator) == 0) separator = ',';

        var rows = new List<string[]>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        void EndCell() { cells.Add(cell.ToString().Trim()); cell.Clear(); }
        void EndRow() { EndCell(); if (cells.Any(c => c != "")) rows.Add(cells.ToArray()); cells.Clear(); }

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; }
                else cell.Append(ch);
            }
            else if (ch == '"' && cell.Length == 0) quoted = true;
            else if (ch == separator) EndCell();
            else if (ch == '\n') EndRow();
            else if (ch != '\r') cell.Append(ch);
        }
        if (cell.Length > 0 || cells.Count > 0) EndRow();
        return rows;
    }

    // ---- Guessing the layout ----

    public static bool TryDate(string text, string format, out DateOnly date)
    {
        var s = text.Trim();
        if (DateOnly.TryParseExact(s, format, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)) return true;
        if (s.Length > 10 && DateTime.TryParseExact(s, [format + " HH:mm:ss", format + " HH:mm", format + "'T'HH:mm:ss", format + " H:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            date = DateOnly.FromDateTime(dt);
            return true;
        }
        return false;
    }

    /// <summary>The first date format that reads every sample, preferring day first over month first.</summary>
    private static string? DateFormatFor(IReadOnlyList<string> samples)
    {
        var values = samples.Where(s => s != "").ToList();
        if (values.Count == 0) return null;
        foreach (var format in DateFormats)
            if (values.All(v => TryDate(v, format, out _))) return format;
        return null;
    }

    /// <summary>Reads an amount: "-125.50", "1,234.56", "9 000,00", "(45.00)", "R 12.00", "45.00-", "45.00 DR". Null when the text is not an amount.</summary>
    public static long? TryMoney(string text)
    {
        var s = text.Trim().Replace(' ', ' ');
        if (s == "") return null;
        var negative = false;
        var upper = s.ToUpperInvariant();
        if (upper.EndsWith("DR")) { negative = true; s = s[..^2]; }
        else if (upper.EndsWith("CR")) s = s[..^2];
        s = s.Trim();
        if (s.StartsWith('(') && s.EndsWith(')')) { negative = true; s = s[1..^1]; }
        if (s.EndsWith('-')) { negative = true; s = s[..^1]; }
        if (s.StartsWith('-')) { negative = !negative; s = s[1..]; }
        else if (s.StartsWith('+')) s = s[1..];
        s = new string(s.Where(c => char.IsDigit(c) || c is '.' or ',').ToArray());
        if (s == "" || !s.Any(char.IsDigit)) return null;

        var lastDot = s.LastIndexOf('.');
        var lastComma = s.LastIndexOf(',');
        var decimalAt = -1;
        if (lastDot >= 0 && lastComma >= 0) decimalAt = Math.Max(lastDot, lastComma);
        else if (lastDot >= 0 || lastComma >= 0)
        {
            var at = Math.Max(lastDot, lastComma);
            var sep = s[at];
            var count = s.Count(c => c == sep);
            var after = s.Length - at - 1;
            if (count == 1 && !(sep == ',' && after == 3)) decimalAt = at;   // "1,234" is a thousand; "12,50" and "12.345" are decimals
        }
        var whole = decimalAt >= 0 ? s[..decimalAt] : s;
        var fraction = decimalAt >= 0 ? s[(decimalAt + 1)..] : "";
        whole = new string(whole.Where(char.IsDigit).ToArray());
        fraction = new string(fraction.Where(char.IsDigit).ToArray());
        if (fraction.Length > 2) fraction = fraction[..2];
        fraction = fraction.PadRight(2, '0');
        if (!long.TryParse(whole == "" ? "0" : whole, NumberStyles.None, CultureInfo.InvariantCulture, out var units)) return null;
        var cents = units * 100 + long.Parse(fraction, CultureInfo.InvariantCulture);
        return negative ? -cents : cents;
    }

    /// <summary>Guesses where the date, description and money are. Null when the table has no column of dates with money beside it.</summary>
    public static BankFormat? Detect(BankTable table)
    {
        var rows = table.Rows;
        var columns = table.Columns;
        if (rows.Count == 0 || columns < 2) return null;

        // The first data row is the first one with something that reads as a date and, somewhere else, an amount.
        int firstData = -1;
        for (var r = 0; r < Math.Min(rows.Count, 30) && firstData < 0; r++)
        {
            var row = rows[r];
            var hasDate = row.Select((v, c) => (v, c)).Any(x => DateFormatFor([x.v]) is not null);
            var hasMoney = row.Select((v, c) => (v, c)).Any(x => DateFormatFor([x.v]) is null && TryMoney(x.v) is not null);
            if (hasDate && hasMoney) firstData = r;
        }
        if (firstData < 0) return null;
        var data = rows.Skip(firstData).Take(200).ToList();
        var headings = firstData > 0 ? rows[firstData - 1] : [];
        string Heading(int c) => c < headings.Length ? headings[c].ToLowerInvariant() : "";

        // The date column: the first one where nearly every non-empty cell reads as a date in one format.
        int dateColumn = -1;
        string? dateFormat = null;
        for (var c = 0; c < columns && dateColumn < 0; c++)
        {
            var values = data.Select(r => c < r.Length ? r[c] : "").Where(v => v != "").ToList();
            if (values.Count == 0) continue;
            var format = DateFormatFor(values);
            if (format is not null && values.Any(v => v.Any(char.IsDigit))) { dateColumn = c; dateFormat = format; }
        }
        if (dateColumn < 0 || dateFormat is null) return null;

        bool IsMoneyColumn(int c)
        {
            if (c == dateColumn) return false;
            var values = data.Select(r => c < r.Length ? r[c] : "").Where(v => v != "").ToList();
            return values.Count > 0 && values.All(v => TryMoney(v) is not null);
        }
        var money = Enumerable.Range(0, columns).Where(IsMoneyColumn).ToList();
        if (money.Count == 0) return null;

        bool Hint(int c, params string[] words) => words.Any(w => Heading(c).Contains(w));
        var balance = money.Where(c => Hint(c, "balance")).ToList();
        var candidates = money.Except(balance).ToList();
        if (candidates.Count == 0) return null;
        int amount = -1, debit = -1, credit = -1;
        var debitHint = candidates.Where(c => Hint(c, "debit", "withdraw", "paid out", "money out", "outflow", "payment")).ToList();
        var creditHint = candidates.Where(c => Hint(c, "credit", "deposit", "paid in", "money in", "inflow", "receipt")).ToList();
        if (debitHint.Count > 0 && creditHint.Count > 0) { debit = debitHint[0]; credit = creditHint[0]; }
        else if (candidates.Count == 1) amount = candidates[0];
        else if (candidates.Any(c => Hint(c, "amount"))) amount = candidates.First(c => Hint(c, "amount"));
        else if (candidates.Count == 2 && balance.Count == 0 && data.All(r => (TryMoney(r.ElementAtOrDefault(candidates[0]) ?? "") is null) || (TryMoney(r.ElementAtOrDefault(candidates[1]) ?? "") is null))) { debit = candidates[0]; credit = candidates[1]; }
        else amount = candidates[0];   // the rest are most likely a running balance and the like

        // The text columns: the description is the one with the longest text (or the one headed so), the memo is the next one.
        var used = new HashSet<int>(money) { dateColumn };
        var texts = Enumerable.Range(0, columns).Where(c => !used.Contains(c) && data.Any(r => c < r.Length && r[c] != ""))
            .OrderByDescending(c => Hint(c, "description", "details", "narrative", "payee", "particulars", "name") ? 1000 : 0)
            .ThenByDescending(c => data.Average(r => c < r.Length ? r[c].Length : 0)).ToList();
        if (texts.Count == 0) return null;
        var description = texts[0];
        var memo = texts.Skip(1).FirstOrDefault(-1);

        return new BankFormat(firstData, dateColumn, dateFormat, description, amount, debit, credit, true, memo);
    }

    // ---- Reading the lines ----

    /// <summary>Reads the lines of a table by a layout. Lines that cannot be read are reported, not returned.</summary>
    public static BankParse Parse(BankTable table, BankFormat format)
    {
        var lines = new List<BankLine>();
        var problems = new List<ImportProblem>();
        for (var r = format.HeaderRows; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            var number = r + 1;
            if (row.All(c => c == "")) continue;
            var dateText = table.Cell(r, format.DateColumn);
            var description = table.Cell(r, format.DescriptionColumn);
            if (dateText == "") { if (description != "") problems.Add(new ImportProblem(number, $"\"{Shorten(description)}\": there is no date.")); continue; }
            if (!TryDate(dateText, format.DateFormat, out var date)) { problems.Add(new ImportProblem(number, $"The date \"{dateText}\" could not be read as {format.DateFormat}.")); continue; }

            long amount;
            if (format.AmountColumn >= 0)
            {
                var value = TryMoney(table.Cell(r, format.AmountColumn));
                if (value is null) { problems.Add(new ImportProblem(number, $"{date:d MMM yyyy}: the amount \"{table.Cell(r, format.AmountColumn)}\" could not be read.")); continue; }
                amount = format.NegativeIsOut ? value.Value : -value.Value;
            }
            else
            {
                var debit = format.DebitColumn >= 0 ? TryMoney(table.Cell(r, format.DebitColumn)) : null;
                var credit = format.CreditColumn >= 0 ? TryMoney(table.Cell(r, format.CreditColumn)) : null;
                amount = -Math.Abs(debit ?? 0) + Math.Abs(credit ?? 0);
            }
            if (amount == 0) { problems.Add(new ImportProblem(number, $"{date:d MMM yyyy}: there is no amount.")); continue; }
            var memo = format.MemoColumn >= 0 ? table.Cell(r, format.MemoColumn) : "";
            lines.Add(new BankLine(number, date, description == "" ? "(no description)" : description, memo == "" ? null : memo, amount));
        }
        return new BankParse(lines, problems);
    }

    private static string Shorten(string s) => s.Length <= 30 ? s : s[..29] + "…";
}

/// <summary>A line of a bank file and the transaction already in the account that it is (null when the account has no such transaction).</summary>
public sealed record BankMatch(BankLine Line, TransactionEntity? Existing);

public sealed partial class BudgetStore
{
    private static string BankFormatKey(Guid accountId) => $"bankformat:{accountId}";

    /// <summary>The file layout last used for an account, so the next file from the same bank needs no setting up.</summary>
    public async Task<BankFormat?> GetBankFormatAsync(Guid accountId, CancellationToken ct = default)
    {
        var json = await GetSettingAsync(BankFormatKey(accountId), ct);
        if (string.IsNullOrEmpty(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<BankFormat>(json); } catch (System.Text.Json.JsonException) { return null; }
    }

    public Task SaveBankFormatAsync(Guid accountId, BankFormat format, CancellationToken ct = default) =>
        SetSettingAsync(BankFormatKey(accountId), System.Text.Json.JsonSerializer.Serialize(format), ct);

    /// <summary>
    /// Matches each line to a transaction of the account with the same amount, near the same date (the bank date or the transaction date,
    /// the same day first, then up to <paramref name="days"/> days apart). A transaction is matched once, and one not yet cleared is
    /// preferred to one already cleared. Expected transactions (no date yet) can be matched too: the bank has now told when they happened.
    /// </summary>
    public async Task<IReadOnlyList<BankMatch>> MatchBankLinesAsync(Guid accountId, IReadOnlyList<BankLine> lines, int days = 3, CancellationToken ct = default)
    {
        var amounts = lines.Select(l => l.Amount).Distinct().ToList();
        var candidates = await db.Transactions.AsNoTracking().Where(t => t.AccountId == accountId && amounts.Contains(t.Amount)).ToListAsync(ct);
        var used = new HashSet<Guid>();
        var result = new List<BankMatch>();
        foreach (var line in lines)
        {
            int Distance(TransactionEntity t)
            {
                var best = int.MaxValue;
                if (!t.IsUndated) best = Math.Abs(t.Date.DayNumber - line.Date.DayNumber);
                if (t.BankDate is { } b) best = Math.Min(best, Math.Abs(b.DayNumber - line.Date.DayNumber));
                return best;
            }
            var pick = candidates.Where(t => !used.Contains(t.Id) && t.Amount == line.Amount && (t.IsUndated || Distance(t) <= days))
                .OrderBy(t => t.IsUndated ? days + 1 : Distance(t)).ThenBy(t => t.IsCleared).FirstOrDefault();
            if (pick is not null) used.Add(pick.Id);
            result.Add(new BankMatch(line, pick));
        }
        return result;
    }

    /// <summary>For each line, whether the account already holds it (see <see cref="MatchBankLinesAsync"/>).</summary>
    public async Task<bool[]> FindExistingAsync(Guid accountId, IReadOnlyList<BankLine> lines, CancellationToken ct = default) =>
        (await MatchBankLinesAsync(accountId, lines, 3, ct)).Select(m => m.Existing is not null).ToArray();

    /// <summary>
    /// Applies a reconciliation in one save (so Ctrl+Z takes it all back): the matched transactions become cleared, with the bank's date, and the
    /// lines the account did not have are added as new, cleared transactions. Returns how many were cleared and how many added.
    /// </summary>
    public async Task<(int Cleared, int Added)> ApplyBankMatchesAsync(Guid accountId, IEnumerable<BankMatch> clear, IEnumerable<BankLine> add, CancellationToken ct = default)
    {
        var clearing = clear.Where(m => m.Existing is not null).ToList();
        var ids = clearing.Select(m => m.Existing!.Id).ToList();
        var entities = await db.Transactions.Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var cleared = 0;
        foreach (var match in clearing)
        {
            if (!entities.TryGetValue(match.Existing!.Id, out var t) || t.IsReconciled) continue;
            var changed = false;
            if (t.IsUndated) { t.IsUndated = false; t.Date = match.Line.Date; changed = true; }   // the bank says when it happened
            if (t.BankDate is null) { t.BankDate = match.Line.Date; changed = true; }
            if (!t.IsCleared) { t.IsCleared = true; changed = true; }
            if (changed) cleared++;
        }

        var payees = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextOrder = new Dictionary<DateOnly, int>();
        var added = 0;
        foreach (var line in add)
        {
            if (!nextOrder.TryGetValue(line.Date, out var order)) order = await NextDayOrderAsync(line.Date, ct);
            nextOrder[line.Date] = order + 2;
            var entity = new TransactionEntity { AccountId = accountId, Date = line.Date, DayOrder = order, BankDate = line.Date, Payee = line.Description, Memo = line.Memo, Amount = line.Amount, IsCleared = true };
            db.Transactions.Add(entity);
            db.SplitLines.Add(new SplitLineEntity { TransactionId = entity.Id, BudgetItemId = null, Amount = entity.Amount });
            payees.Add(line.Description);
            added++;
        }
        foreach (var payee in payees) await EnsurePayeeAsync(payee, ct);
        await db.SaveChangesAsync(ct);
        return (cleared, added);
    }
}
