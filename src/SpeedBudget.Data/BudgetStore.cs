using Microsoft.EntityFrameworkCore;
using SpeedBudget.Core;

namespace SpeedBudget.Data;

public sealed record TransactionDetail(TransactionEntity Transaction, IReadOnlyList<SplitLineEntity> Lines);

public sealed record PayeeHint(string Payee, Guid? BudgetItemId, Guid AccountId);

/// <summary>Opens the local database and gives this installation a stable device id.</summary>
public static class LocalDatabase
{
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpeedBudget");

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
        directory ??= DefaultDirectory;
        var clock = new HlcClock(GetOrCreateDeviceId(directory));
        var options = new DbContextOptionsBuilder<SpeedBudgetDb>()
            .UseSqlite($"Data Source={Path.Combine(directory, "budget.db")}")
            .Options;
        var db = new SpeedBudgetDb(options, clock);
        db.Database.Migrate();
        return db;
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
            items.Select(i => new BudgetItem(i.Id, i.Name, i.Kind)).ToList(),
            markers.Select(m => new MonthMarker(m.Id, m.Date, m.DayOrder, m.Label)).ToList(),
            transactions.Select(t => new Core.Transaction(
                t.Id, t.AccountId, t.Date, t.DayOrder, t.Amount,
                splitsByTransaction[t.Id].Select(s => new SplitLine(s.BudgetItemId, s.Amount)).ToList())).ToList(),
            allocations.Select(a => new Allocation(a.MonthId, a.BudgetItemId, a.Amount)).ToList(),
            holds.Select(h => new IncomeHold(h.MonthId, h.IncomeItemId, h.Amount)).ToList());
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

    /// <summary>Creates the starter groups and items, based on a typical household budget.</summary>
    public async Task SeedStarterItemsAsync(CancellationToken ct = default)
    {
        foreach (var (groupName, kind, items) in StarterItems.Groups)
        {
            var group = await AddGroupAsync(groupName, kind, ct);
            foreach (var name in items) await AddItemAsync(group.Id, null, name, kind, ct);
        }
    }

    /// <summary>Sets the budgeted amount for an item in a month, creating the row if needed.</summary>
    public async Task SetAllocationAsync(Guid monthId, Guid itemId, long amount, CancellationToken ct = default)
    {
        var row = await db.Allocations.FirstOrDefaultAsync(a => a.MonthId == monthId && a.BudgetItemId == itemId, ct);
        if (row is null) db.Allocations.Add(new AllocationEntity { MonthId = monthId, BudgetItemId = itemId, Amount = amount });
        else row.Amount = amount;
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
        return changes.OrderBy(c => c.UpdatedAt, StringComparer.Ordinal).ToList();
    }
}
