using Microsoft.EntityFrameworkCore;
using SpeedBudget.Core;

namespace SpeedBudget.Data;

public sealed record TransactionDetail(TransactionEntity Transaction, IReadOnlyList<SplitLineEntity> Lines);

/// <summary>What an undo or redo reversed, in words, and whether it touched accounts (so the account list should be rebuilt).</summary>
public sealed record UndoResult(string Description, bool TouchedAccounts);

public sealed record PayeeHint(string Payee, Guid? BudgetItemId, Guid AccountId);

/// <summary>Opens the local database and gives this installation a stable device id.</summary>
public static class LocalDatabase
{
    /// <summary>Where the database lives. The SPEEDBUDGET_DATA environment variable overrides it, for testing.</summary>
    public static string DefaultDirectory =>
        Environment.GetEnvironmentVariable("SPEEDBUDGET_DATA") is { Length: > 0 } custom
            ? custom
            : s_default.Value;

    private static readonly Lazy<string> s_default = new(() => ResolveDirectory(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));

    /// <summary>The data folder's name. It is separate from the program's folder (SpeedBudget-App), so uninstalling never touches it.</summary>
    public const string DataFolderName = "SpeedBudget-Data";

    private const string OldDataFolderName = "SpeedBudget";

    /// <summary>
    /// The data folder under the given local app data folder. Versions before 0.9.5 kept the data in a folder called
    /// SpeedBudget; if that is found and the new one does not exist yet, it is moved across (or, if it cannot be moved,
    /// used where it is, so nothing is ever lost). A short README is left in the folder to say what it is.
    /// </summary>
    public static string ResolveDirectory(string localAppData)
    {
        var current = Path.Combine(localAppData, DataFolderName);
        var old = Path.Combine(localAppData, OldDataFolderName);
        var result = current;

        if (!Directory.Exists(current) && Directory.Exists(old))
        {
            try { Directory.Move(old, current); }
            catch { result = old; }   // for example the old program is still running and holds the file open: try again next time
        }

        try
        {
            Directory.CreateDirectory(result);
            var readme = Path.Combine(result, "README.txt");
            if (!File.Exists(readme))
                File.WriteAllLines(readme,
                [
                    "This folder holds your Speed Budget data (your budget is the file budget.db) and its backups.",
                    "Please do not delete it. The Speed Budget program itself is kept in the SpeedBudget-App folder next to this one,",
                    "so installing, updating or uninstalling the program never touches this data.",
                ]);
        }
        catch { /* the README is a courtesy; the app works without it */ }
        return result;
    }

    /// <summary>Reads or creates the device id kept beside the database file.</summary>
    public static string GetOrCreateDeviceId(string directory)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "device-id");
        if (File.Exists(file)) return File.ReadAllText(file).Trim();
        var id = Guid.NewGuid().ToString("N");
        File.WriteAllText(file, id);
        return id;
    }

    public static SpeedBudgetDb Open(string? directory = null)
    {
        // A restore chosen in the app is swapped in here, before the database is opened.
        BackupService.ApplyPendingRestore(directory ?? DefaultDirectory, Path.Combine(directory ?? DefaultDirectory, "budget.db"));
        var db = OpenWithoutMigrating(directory);
        db.Database.Migrate();
        return db;
    }

    /// <summary>
    /// A second context on the same file, for background reads (such as the pinned-items strip) that must not
    /// share a context with whatever the user is editing. Call after Open has migrated the database.
    /// </summary>
    public static SpeedBudgetDb OpenForReading(string? directory = null) => OpenWithoutMigrating(directory);

    private static SpeedBudgetDb OpenWithoutMigrating(string? directory)
    {
        directory ??= DefaultDirectory;
        var clock = new HlcClock(GetOrCreateDeviceId(directory));
        var options = new DbContextOptionsBuilder<SpeedBudgetDb>()
            .UseSqlite($"Data Source={Path.Combine(directory, "budget.db")}")
            .Options;
        return new SpeedBudgetDb(options, clock);
    }
}

/// <summary>Reads and writes budget data, hiding sync bookkeeping from the rest of the app.</summary>
public sealed class BudgetStore(SpeedBudgetDb db)
{
    /// <summary>Loads everything the budget engine needs.</summary>
    public async Task<BudgetSnapshot> LoadSnapshotAsync(CancellationToken ct = default)
    {
        var accounts = await db.Accounts.AsNoTracking().ToListAsync(ct);
        var items = await db.Items.AsNoTracking().ToListAsync(ct);
        var markers = await db.Markers.AsNoTracking().ToListAsync(ct);
        var allocations = await db.Allocations.AsNoTracking().ToListAsync(ct);
        var holds = await db.Holds.AsNoTracking().ToListAsync(ct);
        var transactions = await db.Transactions.AsNoTracking().ToListAsync(ct);
        var splitsByTransaction = (await db.SplitLines.AsNoTracking().ToListAsync(ct))
            .ToLookup(s => s.TransactionId);

        return new BudgetSnapshot(
            accounts.Select(a => new Account(a.Id, a.Name, a.IsOnBudget)).ToList(),
            items.Select(i => new BudgetItem(i.Id, i.DisplayName, i.Kind)).ToList(),
            markers.Select(ToCore).ToList(),
            transactions.Select(t => new Core.Transaction(
                t.Id, t.AccountId, t.Date, t.DayOrder, t.Amount,
                splitsByTransaction[t.Id].Select(s => new SplitLine(s.BudgetItemId, s.Amount)).ToList())).ToList(),
            allocations.Select(a => new Allocation(a.MonthId, a.BudgetItemId, a.Amount)).ToList(),
            holds.Select(h => new IncomeHold(h.MonthId, h.IncomeItemId, h.Amount)).ToList(),
            await GetStoredFirstMonthLabelAsync(ct) ?? "",
            await GetSettingAsync(FirstMonthRenamedKey, ct) == "1",
            await GetBudgetStartSettingAsync(ct),
            await GetBudgetStartMonthSettingAsync(ct));
    }

    private static MonthMarker ToCore(MonthMarkerEntity m) => new(m.Id, m.Date, m.DayOrder, m.Label);

    private const string FirstMonthLabelKey = "firstMonthLabel";
    private const string FirstMonthRenamedKey = "firstMonthRenamed";
    private const string BudgetStartKey = "budgetStart";
    private const string BudgetStartMonthKey = "budgetStartMonth";

    public async Task<string?> GetSettingAsync(string key, CancellationToken ct = default) =>
        (await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct))?.Value;

    /// <summary>Saves a synced setting. A null value removes it.</summary>
    public async Task SetSettingAsync(string key, string? value, CancellationToken ct = default)
    {
        // A removed setting stays behind as a hidden tombstone with the same id, so look for it too: adding a second row
        // with that id would fail. Setting it again brings the tombstone back.
        var row = await db.Settings.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Key == key, ct);
        if (value is null) { if (row is { IsDeleted: false }) db.Settings.Remove(row); }
        else if (row is null) db.Settings.Add(new SettingEntity { Id = SettingEntity.IdFor(key), Key = key, Value = value });
        else
        {
            row.Value = value;
            row.IsDeleted = false;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The month the user said the first budget transaction belongs to (or a first-month name typed by hand).</summary>
    private async Task<string?> GetStoredFirstMonthLabelAsync(CancellationToken ct = default) => await GetSettingAsync(FirstMonthLabelKey, ct);

    /// <summary>The name of the first month as the budget now shows it, worked out from the transactions and the month lines.</summary>
    public async Task<string> GetFirstMonthLabelAsync(CancellationToken ct = default) =>
        BudgetEngine.FirstMonthLabelFor(await LoadSnapshotAsync(ct));

    /// <summary>Saves the month the user said the first budget transaction belongs to. It names the first month from then on.</summary>
    public async Task SetFirstMonthLabelAsync(string label, CancellationToken ct = default)
    {
        await SetSettingAsync(FirstMonthLabelKey, label, ct);
        await SetSettingAsync(FirstMonthRenamedKey, null, ct);
    }

    // ---- The start of the budget ----

    private async Task<DateOnly?> GetBudgetStartSettingAsync(CancellationToken ct = default) =>
        DateOnly.TryParseExact(await GetSettingAsync(BudgetStartKey, ct), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var start) ? start : null;

    private async Task<DateOnly?> GetBudgetStartMonthSettingAsync(CancellationToken ct = default) =>
        DateOnly.TryParseExact(await GetSettingAsync(BudgetStartMonthKey, ct), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var month) ? month : null;

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static DateOnly FirstOf(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>The month the budget starts in: the first month. It is the month the start date falls in unless another was chosen.</summary>
    public async Task<DateOnly> GetBudgetStartMonthAsync(CancellationToken ct = default) =>
        await GetBudgetStartMonthSettingAsync(ct) ?? FirstOf(await EnsureBudgetStartAsync(ct));

    /// <summary>
    /// The date the budget starts on. The first month is the month it falls in, and nothing can come before it. A budget
    /// that has none yet (one made before the start date existed) gets one worked out from its data, once.
    /// </summary>
    public async Task<DateOnly> EnsureBudgetStartAsync(CancellationToken ct = default)
    {
        if (await GetBudgetStartSettingAsync(ct) is { } saved) return saved;

        var label = BudgetEngine.FirstMonthLabelFor(await LoadSnapshotAsync(ct));   // no start date yet, so this is worked out from the data
        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = MonthLabels.TryParse(label, out var month) ? month : new DateOnly(today.Year, today.Month, 1);

        // Nothing may come before the start, so an older budget whose data reaches back further starts at its earliest transaction.
        var snapshot = await LoadSnapshotAsync(ct);
        var onBudget = snapshot.Accounts.Where(a => a.IsOnBudget).Select(a => a.Id).ToHashSet();
        if (snapshot.Transactions.Where(t => onBudget.Contains(t.AccountId)).Select(t => (DateOnly?)t.Date).Min() is { } earliest && earliest < start)
            start = new DateOnly(earliest.Year, earliest.Month, 1);
        await SetSettingAsync(BudgetStartKey, start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), ct);
        return start;
    }

    /// <summary>
    /// Moves the start of the budget, and says which month it starts. The date is where the first month begins in the list of
    /// transactions; the month is the one it counts as, which is the month the date falls in unless another is given (a budget
    /// can start on 24 October and be the start of November).
    /// Starting earlier is always possible: months in between are added as month lines, and the old first month keeps its
    /// transactions and budgeted amounts. A later date is refused while a transaction or month line sits before it. Returns a
    /// sentence saying why it was refused, or null when it was done.
    /// </summary>
    public async Task<string?> SetBudgetStartAsync(DateOnly newStart, DateOnly? newMonth = null, CancellationToken ct = default)
    {
        var oldStart = await EnsureBudgetStartAsync(ct);
        var oldMonth = await GetBudgetStartMonthAsync(ct);
        var month = FirstOf(newMonth ?? newStart);
        if (newStart == oldStart && month == oldMonth) return null;
        if (newStart < EarliestMonth || month < EarliestMonth) return $"The budget can't start before {EarliestMonth:MMMM yyyy}.";

        var snapshot = await LoadSnapshotAsync(ct);
        var firstLine = BudgetEngine.SortMarkers(snapshot.Markers).FirstOrDefault();
        if (firstLine is not null)
        {
            if (newStart >= firstLine.Date)
                return $"{firstLine.Label} starts on {firstLine.Date:d MMM yyyy}. The budget has to start before its first month line.";
            if (MonthLabels.TryParse(firstLine.Label, out var lineMonth) && month >= lineMonth)
                return $"The first month has to come before {firstLine.Label}, the month of the next line.";
        }
        if (newStart > oldStart)
        {
            var onBudget = snapshot.Accounts.Where(a => a.IsOnBudget).Select(a => a.Id).ToHashSet();
            var earliest = snapshot.Transactions.Where(t => onBudget.Contains(t.AccountId)).Select(t => (DateOnly?)t.Date).Min();
            if (earliest is { } first && first < newStart)
                return $"There is a transaction on {first:d MMM yyyy}, before that date. Move or delete it first, or choose a date on or before it.";
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (newStart < oldStart && month < oldMonth) await AddLinesForEarlierStartAsync(oldStart, oldMonth, newStart, month, ct);
        await SetSettingAsync(BudgetStartKey, Iso(newStart), ct);
        await SetSettingAsync(BudgetStartMonthKey, month == FirstOf(newStart) ? null : Iso(month), ct);   // nothing to remember when it follows the date
        await transaction.CommitAsync(ct);
        return null;
    }

    /// <summary>
    /// The start moves earlier, and so does the first month. Every month from the new first month up to the old one gets a
    /// line (the new first month needs none), and what was budgeted in the old first month moves to its line. Returns that line.
    /// </summary>
    private async Task<MonthMarkerEntity> AddLinesForEarlierStartAsync(DateOnly oldStart, DateOnly oldMonth, DateOnly newStart, DateOnly newMonth, CancellationToken ct)
    {
        var transactionOrders = await db.Transactions.Where(t => t.Date == oldStart).Select(t => t.DayOrder).ToListAsync(ct);
        MonthMarkerEntity? oldMonthLine = null;
        var index = 0;
        for (var month = newMonth.AddMonths(1); month <= oldMonth; month = month.AddMonths(1), index++)
        {
            // The old first month's line sits where it started, so its transactions stay in it. The others sit within the range the
            // new start adds, in order, after the new start (so what is dated on the new start stays in the new first month).
            var isOld = month == oldMonth;
            var date = isOld ? oldStart : new[] { newStart.AddDays(1), new[] { month, oldStart }.Min() }.Max();
            var line = new MonthMarkerEntity
            {
                Date = date,
                DayOrder = isOld ? Math.Min(0, transactionOrders.Count > 0 ? transactionOrders.Min() : 0) : int.MinValue / 2 + index,
                Label = MonthLabels.For(month),
            };
            await MoveBudgetAsync(isOld ? BudgetEngine.FirstMonthId : BudgetEngine.VirtualMonthId(month), line.Id, ct);
            db.Markers.Add(line);
            if (isOld) oldMonthLine = line;
        }
        await db.SaveChangesAsync(ct);
        return oldMonthLine!;
    }

    /// <summary>The user gave the first month a name of their own, which is then kept as typed.</summary>
    public async Task RenameFirstMonthAsync(string label, CancellationToken ct = default)
    {
        await SetSettingAsync(FirstMonthLabelKey, label, ct);
        await SetSettingAsync(FirstMonthRenamedKey, "1", ct);
    }

    /// <summary>The earliest month the budget can be moved back to.</summary>
    public static readonly DateOnly EarliestMonth = new(1950, 1, 1);

    /// <summary>
    /// Adds an empty month before the first one, so the budget can go back in time. The old first month keeps all its
    /// transactions and budgeted amounts: it becomes a month started by a line. Returns that line, or null if the budget
    /// already reaches back as far as it can.
    /// </summary>
    public async Task<MonthMarkerEntity?> AddEarlierMonthAsync(CancellationToken ct = default)
    {
        var start = await EnsureBudgetStartAsync(ct);
        var oldMonth = await GetBudgetStartMonthAsync(ct);
        var newMonth = oldMonth.AddMonths(-1);
        if (newMonth < EarliestMonth) return null;
        var newStart = FirstOf(start).AddMonths(-1);
        if (newStart >= start) newStart = start.AddDays(-1);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var line = await AddLinesForEarlierStartAsync(start, oldMonth, newStart, newMonth, ct);
        await SetSettingAsync(BudgetStartKey, Iso(newStart), ct);
        await SetSettingAsync(BudgetStartMonthKey, newMonth == FirstOf(newStart) ? null : Iso(newMonth), ct);
        await transaction.CommitAsync(ct);
        return line;
    }

    /// <summary>The balance printed on the account's latest bank statement, in cents, if one was entered.</summary>
    public async Task<long?> GetStatementBalanceAsync(Guid accountId, CancellationToken ct = default) =>
        long.TryParse(await GetSettingAsync($"statement:{accountId}", ct), out var cents) ? cents : null;

    public Task SetStatementBalanceAsync(Guid accountId, long? cents, CancellationToken ct = default) =>
        SetSettingAsync($"statement:{accountId}", cents?.ToString(), ct);

    /// <summary>The cleared and uncleared balances of one account, or of every account when none is given.</summary>
    public async Task<Reconciliation> GetBalancesAsync(Guid? accountId = null, CancellationToken ct = default)
    {
        var accounts = db.Accounts.AsNoTracking().AsQueryable();
        var transactions = db.Transactions.AsNoTracking().AsQueryable();
        if (accountId is { } id)
        {
            accounts = accounts.Where(a => a.Id == id);
            transactions = transactions.Where(t => t.AccountId == id);
        }
        var opening = (await accounts.Select(a => a.OpeningBalance).ToListAsync(ct)).Sum();
        var lines = await transactions.Select(t => new { t.Amount, t.IsCleared }).ToListAsync(ct);
        return Reconciliation.Calculate(opening, lines.Select(l => new ReconcileLine(l.Amount, l.IsCleared)), null);
    }

    /// <summary>
    /// Checks the account against the bank's balance. When the cleared balance matches, every cleared transaction is
    /// marked reconciled and the balance is remembered. Returns the comparison either way.
    /// </summary>
    public async Task<(Reconciliation Result, bool Reconciled)> ReconcileAsync(Guid accountId, long bankBalance, CancellationToken ct = default)
    {
        var account = await db.Accounts.SingleAsync(a => a.Id == accountId, ct);
        var transactions = await db.Transactions.Where(t => t.AccountId == accountId).ToListAsync(ct);
        var result = Reconciliation.Calculate(account.OpeningBalance,
            transactions.Select(t => new ReconcileLine(t.Amount, t.IsCleared)), bankBalance);
        if (!result.IsReconciled) return (result, false);

        foreach (var t in transactions.Where(t => t.IsCleared && !t.IsReconciled)) t.IsReconciled = true;
        await db.SaveChangesAsync(ct);
        await SetStatementBalanceAsync(accountId, bankBalance, ct);
        return (result, true);
    }

    public Task<List<SplitLineEntity>> GetSplitLinesAsync(Guid transactionId, CancellationToken ct = default) =>
        db.SplitLines.AsNoTracking().Where(l => l.TransactionId == transactionId).OrderBy(l => l.Id).ToListAsync(ct);

    /// <summary>Pins an item to the watcher strip, or removes it from the strip.</summary>
    public async Task SetPinnedAsync(Guid itemId, bool pinned, CancellationToken ct = default)
    {
        var item = await db.Items.SingleAsync(i => i.Id == itemId, ct);
        if (item.IsPinned == pinned) return;
        item.IsPinned = pinned;
        // A newly pinned item goes at the end of the strip.
        if (pinned) item.PinOrder = (await db.Items.Where(i => i.IsPinned).Select(i => (int?)i.PinOrder).MaxAsync(ct) ?? 0) + 1;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Puts the items on the watcher strip in the given order (the order they were dragged into).</summary>
    public async Task SetPinOrderAsync(IReadOnlyList<Guid> itemIds, CancellationToken ct = default)
    {
        var items = await db.Items.Where(i => itemIds.Contains(i.Id)).ToListAsync(ct);
        for (var n = 0; n < itemIds.Count; n++)
            if (items.FirstOrDefault(i => i.Id == itemIds[n]) is { } item && item.PinOrder != n + 1) item.PinOrder = n + 1;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The watcher strip shows at most this many budget items.</summary>
    public const int MaxPinnedItems = 5;

    /// <summary>Ids of items pinned to the watcher strip, in the order they were dragged into (new ones last).</summary>
    public async Task<List<Guid>> GetPinnedItemIdsAsync(CancellationToken ct = default) =>
        await db.Items.AsNoTracking().Where(i => i.IsPinned && i.Kind == ItemKind.Expense)
            .OrderBy(i => i.PinOrder).ThenBy(i => i.SortOrder).ThenBy(i => i.Name).Select(i => i.Id).Take(MaxPinnedItems).ToListAsync(ct);

    /// <summary>Replaces a transaction's split lines. They must add up to the transaction's amount.</summary>
    public async Task SetSplitsAsync(Guid transactionId, IReadOnlyList<(Guid? ItemId, long Amount, string? Memo)> lines, CancellationToken ct = default)
    {
        var transaction = await db.Transactions.SingleAsync(t => t.Id == transactionId, ct);
        if (lines.Count == 0) throw new ArgumentException("A transaction needs at least one line.");
        if (lines.Sum(l => l.Amount) != transaction.Amount)
            throw new ArgumentException("Split lines must add up to the transaction amount.");

        db.SplitLines.RemoveRange(await db.SplitLines.Where(l => l.TransactionId == transactionId).ToListAsync(ct));
        foreach (var (itemId, amount, memo) in lines)
            db.SplitLines.Add(new SplitLineEntity { TransactionId = transactionId, BudgetItemId = itemId, Amount = amount, Memo = memo });
        await db.SaveChangesAsync(ct);
    }

    // ---- Payees ----

    public Task<List<PayeeEntity>> GetPayeesAsync(CancellationToken ct = default) =>
        db.Payees.OrderBy(p => p.Name).ToListAsync(ct);

    /// <summary>The payee with this name (ignoring case), created if it does not exist yet.</summary>
    public async Task<PayeeEntity> EnsurePayeeAsync(string name, CancellationToken ct = default)
    {
        name = name.Trim();
        var existing = (await db.Payees.ToListAsync(ct)).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var payee = new PayeeEntity { Name = name };
        db.Payees.Add(payee);
        await db.SaveChangesAsync(ct);
        return payee;
    }

    /// <summary>Makes sure every payee already used on a transaction is in the payee list. Safe to run again.</summary>
    public async Task EnsurePayeesFromTransactionsAsync(CancellationToken ct = default)
    {
        var known = (await db.Payees.Select(p => p.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var used = await db.Transactions.Where(t => t.TransferPairId == null && t.Payee != "").Select(t => t.Payee).Distinct().ToListAsync(ct);
        foreach (var name in used.Select(n => n.Trim()).Where(n => n != "").Distinct(StringComparer.OrdinalIgnoreCase))
            if (known.Add(name)) db.Payees.Add(new PayeeEntity { Name = name });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>How many transactions use each payee, keyed by the lower-case name.</summary>
    public async Task<Dictionary<string, int>> GetPayeeUsageAsync(CancellationToken ct = default)
    {
        var rows = await db.Transactions.AsNoTracking().Where(t => t.Payee != "")
            .GroupBy(t => t.Payee.ToLower()).Select(g => new { Name = g.Key, Count = g.Count() }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Name, r => r.Count);
    }

    public async Task<int> CountPayeeTransactionsAsync(string name, CancellationToken ct = default)
    {
        var lower = name.Trim().ToLower();
        return await db.Transactions.CountAsync(t => t.Payee.ToLower() == lower, ct);
    }

    /// <summary>Renames a payee, and the name on every transaction that uses it. Returns an error sentence, or null.</summary>
    public async Task<string?> RenamePayeeAsync(PayeeEntity payee, string newName, CancellationToken ct = default)
    {
        newName = newName.Trim();
        if (newName == "") return "A counterparty needs a name.";
        var clash = (await db.Payees.ToListAsync(ct)).Any(p => p.Id != payee.Id && string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase));
        if (clash) return "There is already a counterparty with that name.";

        var oldLower = payee.Name.ToLower();
        foreach (var t in await db.Transactions.Where(t => t.Payee.ToLower() == oldLower).ToListAsync(ct)) t.Payee = newName;
        payee.Name = newName;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Sets (or, with an empty string, clears) the icon shown in front of a counterparty's name.</summary>
    public async Task SetPayeeEmojiAsync(PayeeEntity payee, string? emoji, CancellationToken ct = default)
    {
        payee.Emoji = string.IsNullOrEmpty(emoji) ? null : emoji;
        await db.SaveChangesAsync(ct);
    }

    // ---- Expected transactions ----

    /// <summary>The expected transactions of an account, soonest first (those with no date last).</summary>
    public async Task<List<ExpectedTransactionEntity>> GetExpectedAsync(Guid accountId, CancellationToken ct = default) =>
        (await db.Expected.Where(e => e.AccountId == accountId).ToListAsync(ct))
        .OrderBy(e => e.ExpectedDate ?? DateOnly.MaxValue).ThenBy(e => e.UpdatedAt, StringComparer.Ordinal).ToList();

    public async Task<ExpectedTransactionEntity> AddExpectedAsync(ExpectedTransactionEntity expected, CancellationToken ct = default)
    {
        expected.Payee = expected.Payee.Trim();
        expected.Memo = string.IsNullOrWhiteSpace(expected.Memo) ? null : expected.Memo.Trim();
        db.Expected.Add(expected);
        await db.SaveChangesAsync(ct);
        return expected;
    }

    /// <summary>Saves changes made to an expected transaction.</summary>
    public async Task UpdateExpectedAsync(ExpectedTransactionEntity expected, CancellationToken ct = default)
    {
        expected.Payee = expected.Payee.Trim();
        expected.Memo = string.IsNullOrWhiteSpace(expected.Memo) ? null : expected.Memo.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteExpectedAsync(ExpectedTransactionEntity expected, CancellationToken ct = default)
    {
        db.Expected.Remove(expected);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// An expected transaction has happened: it becomes a real transaction on the given date and leaves the expected list.
    /// The transaction is saved with its budget item when it has one.
    /// </summary>
    /// <summary>
    /// Records every dated expected transaction that has come due (on or before <paramref name="today"/>) as a real transaction on its
    /// date. A once-off one then leaves the list; a repeating one moves on to its next date, and if several dates were missed (the app
    /// was not opened) each is recorded on its own date. Housekeeping, so it cannot be undone. Returns how many transactions were added.
    /// </summary>
    public async Task<int> RecordDueExpectedAsync(DateOnly today, CancellationToken ct = default)
    {
        var due = await db.Expected.Where(e => e.Kind != ExpectedKind.Someday && e.ExpectedDate != null).ToListAsync(ct);
        due = due.Where(e => e.ExpectedDate <= today).ToList();
        if (due.Count == 0) return 0;

        var recording = db.Undo.Recording;
        db.Undo.Recording = false;
        var added = 0;
        try
        {
            foreach (var e in due)
            {
                if (!await db.Accounts.AnyAsync(a => a.Id == e.AccountId, ct)) continue;
                var guard = 0;
                while (e.ExpectedDate is { } date && date <= today && guard++ < 1000)
                {
                    await using var scope = await db.Database.BeginTransactionAsync(ct);
                    var transaction = new TransactionEntity
                    {
                        AccountId = e.AccountId, Date = date, Payee = e.Payee, Memo = e.Memo, Amount = e.Amount,
                        DayOrder = await NextDayOrderAsync(date, ct),
                    };
                    await AddTransactionAsync(transaction, [new SplitLineEntity { BudgetItemId = e.BudgetItemId, Amount = e.Amount }], ct);
                    if (e.Kind == ExpectedKind.Repeating) e.ExpectedDate = ExpectedSchedule.Next(date, e.Repeat, e.AnchorDay);
                    else db.Expected.Remove(e);
                    await db.SaveChangesAsync(ct);
                    await scope.CommitAsync(ct);
                    added++;
                    if (e.Kind != ExpectedKind.Repeating) break;
                }
            }
        }
        finally { db.Undo.Recording = recording; }
        return added;
    }

    public async Task<TransactionEntity> RecordExpectedAsync(ExpectedTransactionEntity expected, DateOnly date, int? dayOrder = null, CancellationToken ct = default)
    {
        var transaction = new TransactionEntity
        {
            AccountId = expected.AccountId, Date = date, Payee = expected.Payee, Memo = expected.Memo, Amount = expected.Amount,
            DayOrder = dayOrder ?? await NextDayOrderAsync(date, ct),
        };
        await using var scope = await db.Database.BeginTransactionAsync(ct);
        await AddTransactionAsync(transaction, [new SplitLineEntity { BudgetItemId = expected.BudgetItemId, Amount = expected.Amount }], ct);
        db.Expected.Remove(expected);
        await db.SaveChangesAsync(ct);
        await scope.CommitAsync(ct);
        return transaction;
    }

    /// <summary>
    /// Renames a budget item, gives it an icon, and (when asked) renames its group or moves it to another group, all in one go.
    /// Returns an error sentence, or null.
    /// </summary>
    public async Task<string?> UpdateItemAsync(Guid itemId, string name, string? emoji, Guid groupId, string groupName, CancellationToken ct = default)
    {
        name = name.Trim();
        groupName = groupName.Trim();
        if (name == "") return "A budget item needs a name.";
        if (groupName == "") return "A budget group needs a name.";
        var item = await db.Items.SingleAsync(i => i.Id == itemId, ct);
        var group = await db.Groups.SingleAsync(g => g.Id == groupId, ct);
        if (group.Kind != item.Kind) return "Income items can only be in income groups, and expense items in expense groups.";

        item.Name = name;
        item.Emoji = string.IsNullOrEmpty(emoji) ? null : emoji;
        group.Name = groupName;
        await db.SaveChangesAsync(ct);
        if (item.GroupId != groupId) return await MoveItemToAsync(itemId, groupId, null, null, ct);
        return null;
    }

    /// <summary>Deletes a payee that no transaction uses. Returns false, leaving it alone, if it is in use.</summary>
    public async Task<bool> DeletePayeeAsync(PayeeEntity payee, CancellationToken ct = default)
    {
        if (await CountPayeeTransactionsAsync(payee.Name, ct) > 0) return false;
        db.Payees.Remove(payee);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<List<MonthMarkerEntity>> GetMarkersAsync(CancellationToken ct = default) =>
        db.Markers.OrderBy(m => m.Date).ThenBy(m => m.DayOrder).ThenBy(m => m.Id).ToListAsync(ct);

    /// <summary>
    /// Adds a "Let's start a new month" line at the end of the given date, so everything entered after it
    /// belongs to the new month. Optionally copies the budgeted amounts of the month it interrupts.
    /// </summary>
    public async Task<MonthMarkerEntity> AddMarkerAsync(string label, DateOnly date, bool copyPreviousBudget, CancellationToken ct = default)
    {
        if (await GetBudgetStartSettingAsync(ct) is { } start && date <= start)
            throw new ArgumentException($"A month can't start before the budget does ({start:d MMM yyyy}). Move the start of the budget back first.");
        var order = await NextDayOrderAsync(date, ct);
        var existing = BudgetEngine.SortMarkers((await GetMarkersAsync(ct)).Select(ToCore));
        var previousMonth = BudgetEngine.MonthOf(existing, date, order);

        var marker = new MonthMarkerEntity { Date = date, DayOrder = order, Label = label };

        // If this month was already being budgeted without a line (it was a skipped or a coming month), its budget moves to the new line.
        if (MonthLabels.TryParse(label, out var month)) await MoveBudgetAsync(BudgetEngine.VirtualMonthId(month), marker.Id, ct);

        db.Markers.Add(marker);
        await db.SaveChangesAsync(ct);
        if (copyPreviousBudget) await CopyBudgetAsync(previousMonth, marker.Id, ct);
        return marker;
    }

    /// <summary>Moves everything budgeted in one month (its amounts and income holds) to another month id.</summary>
    private async Task MoveBudgetAsync(Guid from, Guid to, CancellationToken ct)
    {
        foreach (var a in await db.Allocations.Where(a => a.MonthId == from).ToListAsync(ct)) a.MonthId = to;
        foreach (var h in await db.Holds.Where(h => h.MonthId == from).ToListAsync(ct)) h.MonthId = to;
    }

    /// <summary>
    /// The first versions kept budgets for the next three months under three fixed ids. Months are now identified by their
    /// calendar month, so this moves any such budgets to the right month. It runs once.
    /// </summary>
    public async Task MigrateLegacyPlannedMonthsAsync(CancellationToken ct = default)
    {
        const string flag = "plannedMonthsMigrated";
        if (await GetSettingAsync(flag, ct) == "1") return;

        var legacy = Enumerable.Range(1, BudgetEngine.LegacyPlannedMonthCount).Select(BudgetEngine.LegacyPlannedMonthId).ToList();
        if (await db.Allocations.AnyAsync(a => legacy.Contains(a.MonthId), ct) || await db.Holds.AnyAsync(h => legacy.Contains(h.MonthId), ct))
        {
            // The nth planned month was the nth month after the last month line.
            var months = BudgetEngine.Calculate(await LoadSnapshotAsync(ct));
            var today = DateOnly.FromDateTime(DateTime.Today);
            var last = MonthLabels.TryParse(months[^1].Label, out var parsed) ? parsed : new DateOnly(today.Year, today.Month, 1);
            for (var k = 1; k <= legacy.Count; k++)
                await MoveBudgetAsync(legacy[k - 1], BudgetEngine.VirtualMonthId(last.AddMonths(k)), ct);
            await db.SaveChangesAsync(ct);
        }
        await SetSettingAsync(flag, "1", ct);
    }

    /// <summary>Copies one month's budgeted and expected amounts into another, skipping items already set there.</summary>
    public async Task CopyBudgetAsync(Guid fromMonthId, Guid toMonthId, CancellationToken ct = default)
    {
        var source = await db.Allocations.Where(a => a.MonthId == fromMonthId).ToListAsync(ct);
        var alreadySet = (await db.Allocations.Where(a => a.MonthId == toMonthId).ToListAsync(ct))
            .Select(a => a.BudgetItemId).ToHashSet();
        foreach (var a in source.Where(a => !alreadySet.Contains(a.BudgetItemId)))
            db.Allocations.Add(new AllocationEntity { MonthId = toMonthId, BudgetItemId = a.BudgetItemId, Amount = a.Amount });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Moves a month line to a new spot. Refused (false) if that would take it past or onto another line.
    /// </summary>
    public async Task<bool> MoveMarkerAsync(MonthMarkerEntity marker, SortKey key, CancellationToken ct = default)
    {
        if (await GetBudgetStartSettingAsync(ct) is { } start && key.Date <= start) return false;   // a month can't start before the budget does
        var others = (await GetMarkersAsync(ct)).Where(m => m.Id != marker.Id).Select(m => new SortKey(m.Date, m.DayOrder));
        if (!MarkerPlacement.FitsBetweenOtherLines(key, new SortKey(marker.Date, marker.DayOrder), others)) return false;
        marker.Date = key.Date;
        marker.DayOrder = key.DayOrder;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Deletes a month line, merging its month into the one before. The budgeted amounts and income holds of the
    /// two months are added together.
    /// </summary>
    public async Task DeleteMarkerAsync(MonthMarkerEntity marker, CancellationToken ct = default)
    {
        var sorted = BudgetEngine.SortMarkers((await GetMarkersAsync(ct)).Select(ToCore)).ToList();
        var index = sorted.FindIndex(m => m.Id == marker.Id);
        var previousMonth = index > 0 ? sorted[index - 1].Id : BudgetEngine.FirstMonthId;

        var previousAllocations = await db.Allocations.Where(a => a.MonthId == previousMonth).ToListAsync(ct);
        foreach (var a in await db.Allocations.Where(a => a.MonthId == marker.Id).ToListAsync(ct))
        {
            var target = previousAllocations.FirstOrDefault(p => p.BudgetItemId == a.BudgetItemId);
            if (target is null) a.MonthId = previousMonth;
            else { target.Amount += a.Amount; db.Allocations.Remove(a); }
        }

        var previousHolds = await db.Holds.Where(h => h.MonthId == previousMonth).ToListAsync(ct);
        foreach (var h in await db.Holds.Where(h => h.MonthId == marker.Id).ToListAsync(ct))
        {
            var target = previousHolds.FirstOrDefault(p => p.IncomeItemId == h.IncomeItemId);
            if (target is null) h.MonthId = previousMonth;
            else { target.Amount += h.Amount; db.Holds.Remove(h); }
        }

        db.Markers.Remove(marker);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    // ---- Undo and redo ----

    /// <summary>True while something is being edited in memory and not yet saved; undoing then would save it too.</summary>
    public bool HasUnsavedEdits => db.ChangeTracker.HasChanges();

    public bool CanUndo => db.Undo.CanUndo;
    public bool CanRedo => db.Undo.CanRedo;

    /// <summary>Starts keeping the history for Ctrl+Z. Called once the app has finished starting up.</summary>
    public void StartRecordingUndo() => db.Undo.Recording = true;

    public void ClearUndo() => db.Undo.Clear();

    /// <summary>
    /// Reverses the latest action (or, with redo, puts back the one just undone): every row it changed goes back to how it was,
    /// and every row it added is removed. Returns what was reversed, or null when there is nothing to do.
    /// </summary>
    public async Task<UndoResult?> UndoAsync(bool redo = false, CancellationToken ct = default)
    {
        var log = db.Undo;
        var unit = log.Pop(redo);
        if (unit is null) return null;

        log.Reversing = redo;
        try
        {
            for (var i = unit.Rows.Count - 1; i >= 0; i--)
            {
                var row = unit.Rows[i];
                if (await db.FindAsync(row.EntityType, [row.Id], ct) is not SyncEntity entity) continue;
                if (row.WasAdded) { entity.IsDeleted = true; continue; }

                var entry = db.Entry(entity);
                foreach (var (name, value) in row.Old)
                {
                    if (name is nameof(SyncEntity.Id) or nameof(SyncEntity.UpdatedAt) or nameof(SyncEntity.UpdatedBy)) continue;
                    entry.Property(name).CurrentValue = value;
                }
            }
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            db.ChangeTracker.Clear();
            log.Put(redo, unit);
            throw;
        }
        finally { log.Reversing = null; }

        return new UndoResult(unit.Describe(), unit.Rows.Any(r => r.EntityType == typeof(AccountEntity)));
    }

    /// <summary>True once any transaction exists in an account that is part of the budget.</summary>
    public async Task<bool> HasBudgetTransactionsAsync(CancellationToken ct = default)
    {
        var budgetAccounts = await db.Accounts.Where(a => a.IsOnBudget).Select(a => a.Id).ToListAsync(ct);
        return await db.Transactions.AnyAsync(t => budgetAccounts.Contains(t.AccountId), ct);
    }

    /// <summary>True when nothing has been set up yet, so the first-run welcome should show.</summary>
    public async Task<bool> IsEmptyAsync(CancellationToken ct = default) =>
        !await db.Accounts.AnyAsync(ct) && !await db.Groups.AnyAsync(ct);

    public Task<List<AccountEntity>> GetAccountsAsync(CancellationToken ct = default) =>
        db.Accounts.OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToListAsync(ct);

    /// <summary>Saves the order accounts are listed in: the first id comes first. Accounts not named keep the order they had.</summary>
    public async Task SetAccountOrderAsync(IReadOnlyList<Guid> orderedIds, CancellationToken ct = default)
    {
        var accounts = await db.Accounts.ToListAsync(ct);
        for (var i = 0; i < orderedIds.Count; i++)
            if (accounts.FirstOrDefault(a => a.Id == orderedIds[i]) is { } account && account.SortOrder != i) account.SortOrder = i;
        await db.SaveChangesAsync(ct);
    }

    public Task<List<GroupEntity>> GetGroupsAsync(CancellationToken ct = default) =>
        db.Groups.OrderBy(g => g.SortOrder).ThenBy(g => g.Name).ToListAsync(ct);

    public Task<List<ItemEntity>> GetItemsAsync(CancellationToken ct = default) =>
        db.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Name).ToListAsync(ct);

    public async Task<AccountEntity> AddAccountAsync(string name, AccountType type, bool isOnBudget, long openingBalance = 0, CancellationToken ct = default)
    {
        var next = await db.Accounts.AnyAsync(ct) ? await db.Accounts.MaxAsync(a => a.SortOrder, ct) + 1 : 0;
        var account = new AccountEntity { Name = name, Type = type, IsOnBudget = isOnBudget, OpeningBalance = openingBalance, SortOrder = next };
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account;
    }

    public async Task<GroupEntity> AddGroupAsync(string name, ItemKind kind, CancellationToken ct = default)
    {
        var next = await db.Groups.AnyAsync(ct) ? await db.Groups.MaxAsync(g => g.SortOrder, ct) + 1 : 0;
        var group = new GroupEntity { Name = name, Kind = kind, SortOrder = next };
        db.Groups.Add(group);
        await db.SaveChangesAsync(ct);
        return group;
    }

    public async Task RenameGroupAsync(Guid groupId, string name, CancellationToken ct = default)
    {
        name = name.Trim();
        if (name == "") return;
        var group = await db.Groups.SingleAsync(g => g.Id == groupId, ct);
        if (group.Name == name) return;
        group.Name = name;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Renames a budget item and/or changes its icon (null leaves that part as it is; an empty icon removes it).</summary>
    public async Task EditItemAsync(Guid itemId, string? name, string? emoji, CancellationToken ct = default)
    {
        var item = await db.Items.SingleAsync(i => i.Id == itemId, ct);
        if (name is not null && name.Trim() != "") item.Name = name.Trim();
        if (emoji is not null) item.Emoji = emoji == "" ? null : emoji;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds an item to a group, or under another item of that group when parentId is given.</summary>
    public async Task<ItemEntity> AddItemAsync(Guid groupId, Guid? parentId, string name, ItemKind kind, CancellationToken ct = default)
    {
        var siblings = db.Items.Where(i => i.GroupId == groupId && i.ParentItemId == parentId);
        var next = await siblings.AnyAsync(ct) ? await siblings.MaxAsync(i => i.SortOrder, ct) + 1 : 0;
        var item = new ItemEntity { GroupId = groupId, ParentItemId = parentId, Name = name, Kind = kind, SortOrder = next };
        db.Items.Add(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    /// <summary>Deletes an account, or closes it instead when it already has transactions. Returns true if deleted.</summary>
    public Task<bool> AccountHasTransactionsAsync(Guid accountId, CancellationToken ct = default) =>
        db.Transactions.AnyAsync(t => t.AccountId == accountId, ct);

    public async Task<bool> DeleteOrCloseAccountAsync(AccountEntity account, CancellationToken ct = default)
    {
        if (await db.Transactions.AnyAsync(t => t.AccountId == account.Id, ct))
        {
            account.IsClosed = true;
            await db.SaveChangesAsync(ct);
            return false;
        }
        db.Accounts.Remove(account);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Why a budget item can't be deleted (as a short phrase), or null when it can: it has never been used. Budget of zero and income held
    /// of zero don't count as use. <paramref name="ignoreSubItemsIn"/> lists items being deleted along with it, whose sub-items don't block it.
    /// </summary>
    public async Task<string?> WhyItemCannotBeDeletedAsync(Guid itemId, ISet<Guid>? ignoreSubItemsIn = null, CancellationToken ct = default)
    {
        if (await db.SplitLines.AnyAsync(s => s.BudgetItemId == itemId, ct)) return "it has transactions";
        if (await db.Expected.AnyAsync(e => e.BudgetItemId == itemId, ct)) return "an expected transaction uses it";
        if (await db.Allocations.AnyAsync(a => a.BudgetItemId == itemId && a.Amount != 0, ct)) return "it has budget on it";
        if (await db.Holds.AnyAsync(h => h.IncomeItemId == itemId && h.Amount != 0, ct)) return "it has income held on it";
        var children = await db.Items.Where(i => i.ParentItemId == itemId).Select(i => i.Id).ToListAsync(ct);
        if (children.Any(c => ignoreSubItemsIn is null || !ignoreSubItemsIn.Contains(c))) return "it has sub-items";
        return null;
    }

    /// <summary>Deletes an item that has never been used. Returns false, changing nothing, when it has been used.</summary>
    public async Task<bool> DeleteItemAsync(ItemEntity item, CancellationToken ct = default)
    {
        if (await WhyItemCannotBeDeletedAsync(item.Id, null, ct) is not null) return false;
        await RemoveItemRowsAsync(item, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task RemoveItemRowsAsync(ItemEntity item, CancellationToken ct)
    {
        // Budget of zero and nothing held are leftovers of typing and clearing a figure: they go with the item.
        db.Allocations.RemoveRange(await db.Allocations.Where(a => a.BudgetItemId == item.Id).ToListAsync(ct));
        db.Holds.RemoveRange(await db.Holds.Where(h => h.IncomeItemId == item.Id).ToListAsync(ct));
        db.Items.Remove(item);
    }

    /// <summary>One-off clean-up: budget items that were hidden by an earlier version are shown again, as every item is now.</summary>
    public async Task<int> ShowAllItemsAsync(CancellationToken ct = default)
    {
        var hidden = await db.Items.Where(i => i.IsArchived).ToListAsync(ct);
        foreach (var item in hidden) item.IsArchived = false;
        if (hidden.Count > 0) await db.SaveChangesAsync(ct);
        return hidden.Count;
    }

    /// <summary>Changes all the details of an account.</summary>
    public async Task UpdateAccountAsync(Guid accountId, string name, AccountType type, bool isOnBudget, long openingBalance, bool isClosed, CancellationToken ct = default)
    {
        var account = await db.Accounts.SingleAsync(a => a.Id == accountId, ct);
        account.Name = name.Trim();
        account.Type = type;
        account.IsOnBudget = isOnBudget;
        account.OpeningBalance = openingBalance;
        account.IsClosed = isClosed;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Moves a group to just before (or after) another group of the same kind, renumbering the groups.</summary>
    public async Task MoveGroupToAsync(Guid groupId, Guid targetGroupId, bool after, CancellationToken ct = default)
    {
        if (groupId == targetGroupId) return;
        var all = await GetGroupsAsync(ct);
        var moving = all.SingleOrDefault(g => g.Id == groupId);
        var target = all.SingleOrDefault(g => g.Id == targetGroupId);
        if (moving is null || target is null || moving.Kind != target.Kind) return;

        all.Remove(moving);
        all.Insert(all.IndexOf(target) + (after ? 1 : 0), moving);
        for (var n = 0; n < all.Count; n++)
            if (all[n].SortOrder != n) all[n].SortOrder = n;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Why a group and its items can't be deleted (a sentence naming the item in the way), or null when none of them has ever been used.</summary>
    public async Task<string?> WhyGroupCannotBeDeletedAsync(Guid groupId, CancellationToken ct = default)
    {
        var items = await db.Items.Where(i => i.GroupId == groupId).ToListAsync(ct);
        var going = items.Select(i => i.Id).ToHashSet();
        foreach (var item in items)
            if (await WhyItemCannotBeDeletedAsync(item.Id, going, ct) is { } why)
                return $"{item.Name} can't be deleted because {why}.";
        return null;
    }

    /// <summary>
    /// Deletes a group together with its items, but only when none of them has ever been used. Otherwise nothing is deleted and the
    /// reason is returned, naming the item in the way.
    /// </summary>
    public async Task<(bool Deleted, string? Blocker)> DeleteGroupWithItemsAsync(Guid groupId, CancellationToken ct = default)
    {
        var group = await db.Groups.SingleAsync(g => g.Id == groupId, ct);
        var items = await db.Items.Where(i => i.GroupId == groupId).ToListAsync(ct);
        if (await WhyGroupCannotBeDeletedAsync(groupId, ct) is { } blocker) return (false, blocker);

        foreach (var item in items) await RemoveItemRowsAsync(item, ct);
        db.Groups.Remove(group);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>Deletes a group. Refused (false) while it still has items.</summary>
    public async Task<bool> DeleteGroupAsync(GroupEntity group, CancellationToken ct = default)
    {
        if (await db.Items.AnyAsync(i => i.GroupId == group.Id, ct)) return false;
        db.Groups.Remove(group);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Swaps an item with its previous or next sibling. Returns false at the end of the list.</summary>
    public async Task<bool> MoveItemAsync(ItemEntity item, bool up, CancellationToken ct = default)
    {
        var siblings = await db.Items.Where(i => i.GroupId == item.GroupId && i.ParentItemId == item.ParentItemId)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Name).ToListAsync(ct);
        return await SwapAsync(siblings, item, up, (i, order) => i.SortOrder = order, i => i.SortOrder, ct);
    }

    /// <summary>
    /// Moves an item (and any sub-items under it) to a group, optionally under another item, placing it before the given
    /// sibling or last. Returns an error sentence, or null when moved.
    /// </summary>
    public async Task<string?> MoveItemToAsync(Guid itemId, Guid targetGroupId, Guid? targetParentId, Guid? beforeItemId, CancellationToken ct = default)
    {
        var all = await db.Items.ToListAsync(ct);
        var item = all.Single(i => i.Id == itemId);
        var group = await db.Groups.SingleAsync(g => g.Id == targetGroupId, ct);
        if (group.Kind != item.Kind) return "Income items can only be in income groups, and expense items in expense groups.";

        List<ItemEntity> DescendantsOf(Guid id) =>
            all.Where(i => i.ParentItemId == id).SelectMany(c => new[] { c }.Concat(DescendantsOf(c.Id))).ToList();
        var descendants = DescendantsOf(item.Id);
        if (targetParentId is { } parent && (parent == item.Id || descendants.Any(d => d.Id == parent)))
            return "An item can't be moved inside itself.";

        var siblings = all.Where(i => i.Id != itemId && i.GroupId == targetGroupId && i.ParentItemId == targetParentId)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Name).ToList();
        var index = beforeItemId is { } before ? siblings.FindIndex(s => s.Id == before) : -1;
        siblings.Insert(index < 0 ? siblings.Count : index, item);

        item.GroupId = targetGroupId;
        item.ParentItemId = targetParentId;
        for (var n = 0; n < siblings.Count; n++) siblings[n].SortOrder = n;
        foreach (var d in descendants) d.GroupId = targetGroupId;

        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>A preference of this device only, such as how the budget is laid out. It is not synced.</summary>
    public async Task<string?> GetLocalSettingAsync(string key, CancellationToken ct = default) =>
        (await db.SyncState.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct))?.Value;

    public async Task SetLocalSettingAsync(string key, string value, CancellationToken ct = default)
    {
        var row = await db.SyncState.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null) db.SyncState.Add(new SyncStateEntity { Key = key, Value = value });
        else row.Value = value;
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> MoveGroupAsync(GroupEntity group, bool up, CancellationToken ct = default)
    {
        var all = await GetGroupsAsync(ct);
        return await SwapAsync(all, group, up, (g, order) => g.SortOrder = order, g => g.SortOrder, ct);
    }

    private async Task<bool> SwapAsync<T>(List<T> ordered, T moving, bool up, Action<T, int> setOrder, Func<T, int> getOrder, CancellationToken ct)
        where T : SyncEntity
    {
        var index = ordered.IndexOf(moving);
        var target = up ? index - 1 : index + 1;
        if (index < 0 || target < 0 || target >= ordered.Count) return false;

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        // Renumber the whole list so ties (equal sort orders) never leave the swap looking like a no-op.
        for (var n = 0; n < ordered.Count; n++)
            if (getOrder(ordered[n]) != n) setOrder(ordered[n], n);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Factory reset: deletes every account, budget item, transaction, month line, counterparty and setting, so the app
    /// starts again from a clean slate. This cannot be undone, so callers take a backup first.
    /// </summary>
    public async Task ResetAllAsync(CancellationToken ct = default)
    {
        db.Undo.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SplitLines.ExecuteDeleteAsync(ct);
        await db.Transactions.ExecuteDeleteAsync(ct);
        await db.Allocations.ExecuteDeleteAsync(ct);
        await db.Holds.ExecuteDeleteAsync(ct);
        await db.Markers.ExecuteDeleteAsync(ct);
        await db.Items.ExecuteDeleteAsync(ct);
        await db.Groups.ExecuteDeleteAsync(ct);
        await db.Accounts.ExecuteDeleteAsync(ct);
        await db.Payees.ExecuteDeleteAsync(ct);
        await db.Expected.ExecuteDeleteAsync(ct);
        await db.Settings.ExecuteDeleteAsync(ct);
        await db.SyncState.ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        db.Undo.Clear();
    }

    /// <summary>
    /// Creates what the setup wizard collected. Only adds: an account, group, item or counterparty that already exists
    /// (same name, ignoring case) is left alone, so running the wizard again never duplicates anything.
    /// </summary>
    public async Task<SetupResult> ApplySetupAsync(SetupPlan plan, CancellationToken ct = default)
    {
        static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        int accounts = 0, groups = 0, items = 0;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var existingAccounts = await GetAccountsAsync(ct);
        foreach (var account in plan.Accounts.Where(a => !string.IsNullOrWhiteSpace(a.Name)))
        {
            if (existingAccounts.Any(a => Same(a.Name, account.Name))) continue;
            existingAccounts.Add(await AddAccountAsync(account.Name.Trim(), account.Type, account.IsOnBudget, account.OpeningBalance, ct));
            accounts++;
        }

        var existingGroups = await GetGroupsAsync(ct);
        var existingItems = await GetItemsAsync(ct);
        foreach (var group in plan.Groups.Where(g => !string.IsNullOrWhiteSpace(g.Name)))
        {
            var chosen = group.Items.Where(i => i.Selected && !string.IsNullOrWhiteSpace(i.Name)).ToList();
            var entity = existingGroups.FirstOrDefault(g => g.Kind == group.Kind && Same(g.Name, group.Name));
            if (entity is null)
            {
                if (chosen.Count == 0) continue;   // a group with nothing ticked is not worth creating
                entity = await AddGroupAsync(group.Name.Trim(), group.Kind, ct);
                existingGroups.Add(entity);
                groups++;
            }
            foreach (var item in chosen)
            {
                if (existingItems.Any(i => i.GroupId == entity.Id && i.ParentItemId == null && Same(i.Name, item.Name))) continue;
                var created = await AddItemAsync(entity.Id, null, item.Name.Trim(), group.Kind, ct);
                if (!string.IsNullOrEmpty(item.Emoji))
                {
                    created.Emoji = item.Emoji;
                    await db.SaveChangesAsync(ct);
                }
                existingItems.Add(created);
                items++;
            }
        }

        var known = (await GetPayeesAsync(ct)).Count;
        foreach (var counterparty in plan.Counterparties.Where(c => c.Selected && !string.IsNullOrWhiteSpace(c.Name)))
            await EnsurePayeeAsync(counterparty.Name, ct);
        var counterparties = (await GetPayeesAsync(ct)).Count - known;

        await EnsureBudgetStartAsync(ct);   // a new budget starts in the current month, unless its first transaction says otherwise
        await transaction.CommitAsync(ct);
        return new SetupResult(accounts, groups, items, counterparties);
    }

    /// <summary>Sets the budgeted amount for an item in a month, creating the row if needed.</summary>
    public async Task SetAllocationAsync(Guid monthId, Guid itemId, long amount, CancellationToken ct = default)
    {
        var row = await db.Allocations.FirstOrDefaultAsync(a => a.MonthId == monthId && a.BudgetItemId == itemId, ct);
        if (row is null) db.Allocations.Add(new AllocationEntity { MonthId = monthId, BudgetItemId = itemId, Amount = amount });
        else row.Amount = amount;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds to (or takes from) the budgeted amounts of several items in a month, all in one save. Used to move budget between items.</summary>
    public async Task AdjustAllocationsAsync(Guid monthId, IEnumerable<(Guid ItemId, long Delta)> deltas, CancellationToken ct = default)
    {
        var existing = await db.Allocations.Where(a => a.MonthId == monthId).ToListAsync(ct);
        foreach (var (itemId, delta) in deltas)
        {
            var row = existing.FirstOrDefault(a => a.BudgetItemId == itemId);
            if (row is null)
            {
                row = new AllocationEntity { MonthId = monthId, BudgetItemId = itemId, Amount = delta };
                db.Allocations.Add(row);
                existing.Add(row);
            }
            else row.Amount += delta;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sets many budgeted amounts for a month in one save.</summary>
    public async Task SetAllocationsAsync(Guid monthId, IEnumerable<(Guid ItemId, long Amount)> amounts, CancellationToken ct = default)
    {
        var existing = await db.Allocations.Where(a => a.MonthId == monthId).ToListAsync(ct);
        foreach (var (itemId, amount) in amounts)
        {
            var row = existing.FirstOrDefault(a => a.BudgetItemId == itemId);
            if (row is null) db.Allocations.Add(new AllocationEntity { MonthId = monthId, BudgetItemId = itemId, Amount = amount });
            else row.Amount = amount;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sets how much of a month's income is held for next month. Zero removes the hold.</summary>
    public async Task SetIncomeHoldAsync(Guid monthId, Guid incomeItemId, long amount, CancellationToken ct = default)
    {
        var row = await db.Holds.FirstOrDefaultAsync(h => h.MonthId == monthId && h.IncomeItemId == incomeItemId, ct);
        if (row is null && amount != 0) db.Holds.Add(new IncomeHoldEntity { MonthId = monthId, IncomeItemId = incomeItemId, Amount = amount });
        else if (row is not null && amount == 0) db.Holds.Remove(row);
        else if (row is not null) row.Amount = amount;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds a transaction with its split lines. Split lines must add up to the amount.</summary>
    public async Task<TransactionEntity> AddTransactionAsync(
        TransactionEntity transaction, IEnumerable<SplitLineEntity> splits, CancellationToken ct = default)
    {
        var lines = splits.ToList();
        if (lines.Sum(l => l.Amount) != transaction.Amount)
            throw new ArgumentException("Split lines must add up to the transaction amount.");

        // A typed-in payee joins the payee list, so it can be picked next time.
        if (transaction.TransferPairId is null && !string.IsNullOrWhiteSpace(transaction.Payee))
            await EnsurePayeeAsync(transaction.Payee, ct);

        db.Transactions.Add(transaction);
        foreach (var line in lines)
        {
            line.TransactionId = transaction.Id;
            db.SplitLines.Add(line);
        }
        await db.SaveChangesAsync(ct);
        return transaction;
    }

    /// <summary>Transactions in date order (oldest first), with their split lines. Optionally for one account.</summary>
    public async Task<List<TransactionDetail>> GetTransactionsAsync(Guid? accountId = null, CancellationToken ct = default)
    {
        var query = db.Transactions.AsQueryable();
        if (accountId is { } id) query = query.Where(t => t.AccountId == id);
        var transactions = await query.OrderBy(t => t.Date).ThenBy(t => t.DayOrder).ThenBy(t => t.Id).ToListAsync(ct);

        var wanted = transactions.Select(t => t.Id).ToHashSet();
        var lines = (await db.SplitLines.ToListAsync(ct)).Where(l => wanted.Contains(l.TransactionId)).ToLookup(l => l.TransactionId);
        return transactions.Select(t => new TransactionDetail(t, lines[t.Id].ToList())).ToList();
    }

    /// <summary>The order-in-day for a new row on a date, so it lands after everything already on that day.</summary>
    public async Task<int> NextDayOrderAsync(DateOnly date, CancellationToken ct = default)
    {
        var txOrders = await db.Transactions.Where(t => t.Date == date).Select(t => t.DayOrder).ToListAsync(ct);
        var markerOrders = await db.Markers.Where(m => m.Date == date).Select(m => m.DayOrder).ToListAsync(ct);
        var all = txOrders.Concat(markerOrders).ToList();
        return all.Count == 0 ? 0 : all.Max() + 1;
    }

    /// <summary>Gives a transaction exactly one split line with the given item and amount.</summary>
    public async Task SetSingleSplitAsync(Guid transactionId, Guid? itemId, long amount, CancellationToken ct = default)
    {
        var lines = await db.SplitLines.Where(l => l.TransactionId == transactionId).ToListAsync(ct);
        if (lines.Count == 1)
        {
            lines[0].BudgetItemId = itemId;
            lines[0].Amount = amount;
        }
        else
        {
            foreach (var line in lines) db.SplitLines.Remove(line);
            db.SplitLines.Add(new SplitLineEntity { TransactionId = transactionId, BudgetItemId = itemId, Amount = amount });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes a transaction. Deleting one side of a transfer deletes the other side too.</summary>
    public async Task DeleteTransactionAsync(TransactionEntity transaction, CancellationToken ct = default)
    {
        if (transaction.TransferPairId is { } pair)
            db.Transactions.RemoveRange(await db.Transactions.Where(t => t.TransferPairId == pair).ToListAsync(ct));
        else
            db.Transactions.Remove(transaction);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Moves money between two accounts as a linked pair of transactions. Between two budget accounts, or two
    /// tracked ones, the budget is unaffected. When exactly one side is a budget account, the transfer counts as
    /// spending (to a tracked account) or income (from one) and needs a matching budget item.
    /// </summary>
    public async Task<(TransactionEntity From, TransactionEntity To)> AddTransferAsync(
        Guid fromAccountId, Guid toAccountId, DateOnly date, long amount, string? memo, Guid? budgetItemId,
        int? dayOrder = null, CancellationToken ct = default)
    {
        if (amount < 0) throw new ArgumentException("A transfer amount can't be negative.");
        if (fromAccountId == toAccountId) throw new ArgumentException("Choose two different accounts.");

        var from = await db.Accounts.SingleAsync(a => a.Id == fromAccountId, ct);
        var to = await db.Accounts.SingleAsync(a => a.Id == toAccountId, ct);

        var (fromItem, toItem) = await TransferItemsAsync(from, to, budgetItemId, itemRequired: false, ct);

        var pair = Guid.CreateVersion7();
        var order = dayOrder ?? await NextDayOrderAsync(date, ct);
        var outgoing = new TransactionEntity
        {
            AccountId = from.Id, Date = date, DayOrder = order, Payee = $"Transfer to {to.Name}",
            Memo = memo, Amount = -amount, TransferPairId = pair,
        };
        var incoming = new TransactionEntity
        {
            AccountId = to.Id, Date = date, DayOrder = order, Payee = $"Transfer from {from.Name}",
            Memo = memo, Amount = amount, TransferPairId = pair,
        };
        db.Transactions.AddRange(outgoing, incoming);
        db.SplitLines.Add(new SplitLineEntity { TransactionId = outgoing.Id, BudgetItemId = fromItem, Amount = -amount });
        db.SplitLines.Add(new SplitLineEntity { TransactionId = incoming.Id, BudgetItemId = toItem, Amount = amount });
        await db.SaveChangesAsync(ct);
        return (outgoing, incoming);
    }

    /// <summary>
    /// Which side of a transfer carries a budget item. Only a transfer with exactly one budget account has one: an
    /// expense item when money leaves the budget, an income item when it comes in. A wrong kind of item is refused;
    /// a missing one is refused only when <paramref name="itemRequired"/>.
    /// </summary>
    private async Task<(Guid? FromItem, Guid? ToItem)> TransferItemsAsync(
        AccountEntity from, AccountEntity to, Guid? budgetItemId, bool itemRequired, CancellationToken ct)
    {
        if (from.IsOnBudget == to.IsOnBudget) return (null, null);

        var wantedKind = from.IsOnBudget ? ItemKind.Expense : ItemKind.Income;
        var item = budgetItemId is { } id ? await db.Items.SingleOrDefaultAsync(i => i.Id == id, ct) : null;
        if (item is null ? itemRequired : item.Kind != wantedKind)
            throw new ArgumentException($"Choose an {wantedKind.ToString().ToLowerInvariant()} item for this transfer.");
        return from.IsOnBudget ? (item?.Id, null) : (null, item?.Id);
    }

    /// <summary>The two sides of every transfer: which transaction is in which account.</summary>
    public async Task<List<(Guid PairId, Guid TransactionId, Guid AccountId)>> GetTransferLinksAsync(CancellationToken ct = default) =>
        (await db.Transactions.AsNoTracking().Where(t => t.TransferPairId != null).Select(t => new { t.TransferPairId, t.Id, t.AccountId }).ToListAsync(ct))
        .Select(t => (t.TransferPairId!.Value, t.Id, t.AccountId)).ToList();

    /// <summary>
    /// Applies an edit made to one side of a transfer to the pair. The edited side (whose values have already been
    /// changed) sets the date, memo and amount of both; the other account may be changed; the pair's counterparty names
    /// and the item on the budget side are brought into line. Returns a note for the user when something is still
    /// to be filled in, or null. An invalid edit is undone and refused with an <see cref="ArgumentException"/>.
    /// </summary>
    public async Task<string?> UpdateTransferAsync(Guid sourceId, Guid? otherAccountId, Guid? budgetItemId, CancellationToken ct = default)
    {
        var source = await db.Transactions.SingleAsync(t => t.Id == sourceId, ct);
        var pair = source.TransferPairId ?? throw new ArgumentException("This is not a transfer.");
        var partner = await db.Transactions.SingleAsync(t => t.TransferPairId == pair && t.Id != sourceId, ct);
        try
        {
            if (otherAccountId is { } other) partner.AccountId = other;
            if (source.AccountId == partner.AccountId) throw new ArgumentException("Choose two different accounts.");

            partner.Date = source.Date;
            partner.DayOrder = source.DayOrder;
            partner.Memo = source.Memo;
            partner.Amount = -source.Amount;

            // With an amount of nothing there is no sign to say which side is paying, so each side keeps the role its name gives it.
            var sourceIsOutgoing = source.Amount < 0 || (source.Amount == 0 && source.Payee.StartsWith("Transfer to", StringComparison.Ordinal));
            var (outgoing, incoming) = sourceIsOutgoing ? (source, partner) : (partner, source);
            var from = await db.Accounts.SingleAsync(a => a.Id == outgoing.AccountId, ct);
            var to = await db.Accounts.SingleAsync(a => a.Id == incoming.AccountId, ct);
            outgoing.Payee = $"Transfer to {to.Name}";
            incoming.Payee = $"Transfer from {from.Name}";

            var lines = await db.SplitLines.Where(l => l.TransactionId == source.Id || l.TransactionId == partner.Id).ToListAsync(ct);
            var item = budgetItemId ?? lines.Select(l => l.BudgetItemId).FirstOrDefault(i => i is not null);
            var (fromItem, toItem) = await TransferItemsAsync(from, to, item, itemRequired: false, ct);

            foreach (var (tx, itemId) in new[] { (outgoing, fromItem), (incoming, toItem) })
            {
                var own = lines.Where(l => l.TransactionId == tx.Id).ToList();
                if (own.Count == 1)
                {
                    own[0].BudgetItemId = itemId;
                    own[0].Amount = tx.Amount;
                }
                else
                {
                    db.SplitLines.RemoveRange(own);
                    db.SplitLines.Add(new SplitLineEntity { TransactionId = tx.Id, BudgetItemId = itemId, Amount = tx.Amount });
                }
            }
            await db.SaveChangesAsync(ct);

            return null;   // a transfer is saved with or without a budget item
        }
        catch
        {
            await db.Entry(source).ReloadAsync(ct);   // undo the edit that was refused
            await db.Entry(partner).ReloadAsync(ct);
            throw;
        }
    }

    /// <summary>Takes the budget item off every line of a transaction, as when it moves to an account outside the budget.</summary>
    public async Task ClearLineItemsAsync(Guid transactionId, CancellationToken ct = default)
    {
        foreach (var line in await db.SplitLines.Where(l => l.TransactionId == transactionId).ToListAsync(ct)) line.BudgetItemId = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>For each payee used before, the item and account of its most recent transaction.</summary>
    public async Task<IReadOnlyList<PayeeHint>> GetPayeeHintsAsync(CancellationToken ct = default)
    {
        var transactions = await db.Transactions.AsNoTracking()
            .Where(t => t.TransferPairId == null && t.Payee != "")
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.DayOrder).ThenByDescending(t => t.Id)
            .ToListAsync(ct);
        var lines = (await db.SplitLines.AsNoTracking().ToListAsync(ct)).ToLookup(l => l.TransactionId);

        return transactions
            .GroupBy(t => t.Payee.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(t => new PayeeHint(t.Payee.Trim(), lines[t.Id].FirstOrDefault()?.BudgetItemId, t.AccountId))
            .OrderBy(h => h.Payee, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Every row, including tombstones, changed after the given clock value. This is what sync pushes.</summary>
    public async Task<IReadOnlyList<SyncEntity>> GetChangesAfterAsync(string? cursor, CancellationToken ct = default)
    {
        var after = cursor ?? "";
        var changes = new List<SyncEntity>();
        changes.AddRange(await db.Accounts.IgnoreQueryFilters().Where(e => e.UpdatedAt.CompareTo(after) > 0).ToListAsync(ct));
        changes.AddRange(await db.Groups.IgnoreQueryFilters().Where(e => e.UpdatedAt.CompareTo(after) > 0).ToListAsync(ct));
        changes.AddRange(await db.Items.IgnoreQueryFilters().Where(e => e.UpdatedAt.CompareTo(after) > 0).ToListAsync(ct));
        changes.AddRange(await db.Markers.IgnoreQueryFilters().Where(e => e.UpdatedAt.CompareTo(after) > 0).ToListAsync(ct));
        changes.AddRange(await db.Allocations.IgnoreQueryFilters().Where(e => e.UpdatedAt.CompareTo(after) > 0).ToListAsync(ct));
        changes.AddRange(await db.Holds.IgnoreQueryFilters().Where(e => e.UpdatedAt.CompareTo(after) > 0).ToListAsync(ct));
        changes.AddRange(await db.Transactions.IgnoreQueryFilters().Where(e => e.UpdatedAt.CompareTo(after) > 0).ToListAsync(ct));
        changes.AddRange(await db.SplitLines.IgnoreQueryFilters().Where(e => e.UpdatedAt.CompareTo(after) > 0).ToListAsync(ct));
        changes.AddRange(await db.Payees.IgnoreQueryFilters().Where(e => e.UpdatedAt.CompareTo(after) > 0).ToListAsync(ct));
        changes.AddRange(await db.Expected.IgnoreQueryFilters().Where(e => e.UpdatedAt.CompareTo(after) > 0).ToListAsync(ct));
        changes.AddRange(await db.Settings.IgnoreQueryFilters().Where(e => e.UpdatedAt.CompareTo(after) > 0).ToListAsync(ct));
        return changes.OrderBy(c => c.UpdatedAt, StringComparer.Ordinal).ToList();
    }
}
