using Microsoft.EntityFrameworkCore;
using SpeedBudget.Core;

namespace SpeedBudget.Data;

public sealed record TransactionDetail(TransactionEntity Transaction, IReadOnlyList<SplitLineEntity> Lines);

public sealed record PayeeHint(string Payee, Guid? BudgetItemId, Guid AccountId);

/// <summary>Opens the local database and gives this installation a stable device id.</summary>
public static class LocalDatabase
{
    /// <summary>Where the database lives. The SPEEDBUDGET_DATA environment variable overrides it, for testing.</summary>
    public static string DefaultDirectory =>
        Environment.GetEnvironmentVariable("SPEEDBUDGET_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpeedBudget");

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
            await GetFirstMonthLabelAsync(ct));
    }

    private static MonthMarker ToCore(MonthMarkerEntity m) => new(m.Id, m.Date, m.DayOrder, m.Label);

    public const string DefaultFirstMonthLabel = "January 2027";
    private const string FirstMonthLabelKey = "firstMonthLabel";

    public async Task<string?> GetSettingAsync(string key, CancellationToken ct = default) =>
        (await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct))?.Value;

    /// <summary>Saves a synced setting. A null value removes it.</summary>
    public async Task SetSettingAsync(string key, string? value, CancellationToken ct = default)
    {
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (value is null) { if (row is not null) db.Settings.Remove(row); }
        else if (row is null) db.Settings.Add(new SettingEntity { Id = SettingEntity.IdFor(key), Key = key, Value = value });
        else row.Value = value;
        await db.SaveChangesAsync(ct);
    }

    public async Task<string> GetFirstMonthLabelAsync(CancellationToken ct = default) =>
        await GetSettingAsync(FirstMonthLabelKey, ct) ?? DefaultFirstMonthLabel;

    public Task SetFirstMonthLabelAsync(string label, CancellationToken ct = default) => SetSettingAsync(FirstMonthLabelKey, label, ct);

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
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Ids of items pinned to the watcher strip, in the order they were set up.</summary>
    /// <summary>The watcher strip shows at most this many budget items.</summary>
    public const int MaxPinnedItems = 3;

    public async Task<List<Guid>> GetPinnedItemIdsAsync(CancellationToken ct = default) =>
        await db.Items.AsNoTracking().Where(i => i.IsPinned && !i.IsArchived && i.Kind == ItemKind.Expense)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Name).Select(i => i.Id).Take(MaxPinnedItems).ToListAsync(ct);

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
        var order = await NextDayOrderAsync(date, ct);
        var existing = BudgetEngine.SortMarkers((await GetMarkersAsync(ct)).Select(ToCore));
        var previousMonth = BudgetEngine.MonthOf(existing, date, order);

        var marker = new MonthMarkerEntity { Date = date, DayOrder = order, Label = label };

        // A month started at the end of the timeline takes over the budget already planned for "next month",
        // and the later planned months each move down one place.
        var key = new SortKey(date, order);
        if (!existing.Any() || existing.All(m => new SortKey(m.Date, m.DayOrder) < key))
            await ShiftPlannedBudgetsAsync(marker.Id, ct);

        db.Markers.Add(marker);
        await db.SaveChangesAsync(ct);
        if (copyPreviousBudget) await CopyBudgetAsync(previousMonth, marker.Id, ct);
        return marker;
    }

    /// <summary>Moves planned budgets: the next planned month's go to the new month, and each later one moves down one.</summary>
    private async Task ShiftPlannedBudgetsAsync(Guid newMonthId, CancellationToken ct)
    {
        var planned = Enumerable.Range(1, BudgetEngine.PlannedMonthCount).Select(BudgetEngine.PlannedMonthId).ToList();
        Guid Destination(Guid from)
        {
            var ordinal = planned.IndexOf(from) + 1;
            return ordinal == 1 ? newMonthId : planned[ordinal - 2];
        }

        foreach (var a in await db.Allocations.Where(a => planned.Contains(a.MonthId)).ToListAsync(ct)) a.MonthId = Destination(a.MonthId);
        foreach (var h in await db.Holds.Where(h => planned.Contains(h.MonthId)).ToListAsync(ct)) h.MonthId = Destination(h.MonthId);
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

    /// <summary>True when nothing has been set up yet, so the first-run welcome should show.</summary>
    public async Task<bool> IsEmptyAsync(CancellationToken ct = default) =>
        !await db.Accounts.AnyAsync(ct) && !await db.Groups.AnyAsync(ct);

    public Task<List<AccountEntity>> GetAccountsAsync(CancellationToken ct = default) =>
        db.Accounts.OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToListAsync(ct);

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

    /// <summary>Deletes an item, or archives it instead when it is in use or has sub-items. Returns true if deleted.</summary>
    public async Task<bool> DeleteOrArchiveItemAsync(ItemEntity item, CancellationToken ct = default)
    {
        var inUse = await db.SplitLines.AnyAsync(s => s.BudgetItemId == item.Id, ct)
            || await db.Allocations.AnyAsync(a => a.BudgetItemId == item.Id, ct)
            || await db.Holds.AnyAsync(h => h.IncomeItemId == item.Id, ct)
            || await db.Items.AnyAsync(i => i.ParentItemId == item.Id, ct);
        if (inUse)
        {
            item.IsArchived = true;
            await db.SaveChangesAsync(ct);
            return false;
        }
        db.Items.Remove(item);
        await db.SaveChangesAsync(ct);
        return true;
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
        await db.Settings.ExecuteDeleteAsync(ct);
        await db.SyncState.ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
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
        CancellationToken ct = default)
    {
        if (amount <= 0) throw new ArgumentException("A transfer amount must be more than zero.");
        if (fromAccountId == toAccountId) throw new ArgumentException("Choose two different accounts.");

        var from = await db.Accounts.SingleAsync(a => a.Id == fromAccountId, ct);
        var to = await db.Accounts.SingleAsync(a => a.Id == toAccountId, ct);

        Guid? fromItem = null, toItem = null;
        if (from.IsOnBudget != to.IsOnBudget)
        {
            var wantedKind = from.IsOnBudget ? ItemKind.Expense : ItemKind.Income;
            var item = budgetItemId is { } id ? await db.Items.SingleOrDefaultAsync(i => i.Id == id, ct) : null;
            if (item is null || item.Kind != wantedKind)
                throw new ArgumentException($"Choose an {wantedKind.ToString().ToLowerInvariant()} item for this transfer.");
            if (from.IsOnBudget) fromItem = item.Id; else toItem = item.Id;
        }

        var pair = Guid.CreateVersion7();
        var order = await NextDayOrderAsync(date, ct);
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
        changes.AddRange(await db.Settings.IgnoreQueryFilters().Where(e => e.UpdatedAt.CompareTo(after) > 0).ToListAsync(ct));
        return changes.OrderBy(c => c.UpdatedAt, StringComparer.Ordinal).ToList();
    }
}
