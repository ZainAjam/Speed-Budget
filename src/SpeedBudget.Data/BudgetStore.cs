using Microsoft.EntityFrameworkCore;
using SpeedBudget.Core;

namespace SpeedBudget.Data;

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
