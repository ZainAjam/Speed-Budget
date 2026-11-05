using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SpeedBudget.Data;

public class SpeedBudgetDb : DbContext
{
    private readonly HlcClock _clock;

    public SpeedBudgetDb(DbContextOptions<SpeedBudgetDb> options, HlcClock clock) : base(options) => _clock = clock;

    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();
    public DbSet<GroupEntity> Groups => Set<GroupEntity>();
    public DbSet<ItemEntity> Items => Set<ItemEntity>();
    public DbSet<MonthMarkerEntity> Markers => Set<MonthMarkerEntity>();
    public DbSet<AllocationEntity> Allocations => Set<AllocationEntity>();
    public DbSet<IncomeHoldEntity> Holds => Set<IncomeHoldEntity>();
    public DbSet<TransactionEntity> Transactions => Set<TransactionEntity>();
    public DbSet<SplitLineEntity> SplitLines => Set<SplitLineEntity>();
    public DbSet<PayeeEntity> Payees => Set<PayeeEntity>();
    public DbSet<ExpectedTransactionEntity> Expected => Set<ExpectedTransactionEntity>();
    public DbSet<SettingEntity> Settings => Set<SettingEntity>();
    public DbSet<SyncStateEntity> SyncState => Set<SyncStateEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Every synced table hides tombstones by default and is indexed for "changed since" queries.
        b.Entity<AccountEntity>().HasQueryFilter(e => !e.IsDeleted).HasIndex(e => e.UpdatedAt);
        b.Entity<GroupEntity>().HasQueryFilter(e => !e.IsDeleted).HasIndex(e => e.UpdatedAt);
        b.Entity<ItemEntity>().HasQueryFilter(e => !e.IsDeleted).HasIndex(e => e.UpdatedAt);
        b.Entity<MonthMarkerEntity>().HasQueryFilter(e => !e.IsDeleted).HasIndex(e => e.UpdatedAt);
        b.Entity<AllocationEntity>().HasQueryFilter(e => !e.IsDeleted).HasIndex(e => new { e.MonthId, e.BudgetItemId });
        b.Entity<AllocationEntity>().HasIndex(e => e.UpdatedAt);
        b.Entity<IncomeHoldEntity>().HasQueryFilter(e => !e.IsDeleted).HasIndex(e => new { e.MonthId, e.IncomeItemId });
        b.Entity<IncomeHoldEntity>().HasIndex(e => e.UpdatedAt);
        b.Entity<TransactionEntity>().HasQueryFilter(e => !e.IsDeleted).HasIndex(e => new { e.Date, e.DayOrder });
        b.Entity<TransactionEntity>().HasIndex(e => e.UpdatedAt);
        b.Entity<SplitLineEntity>().HasQueryFilter(e => !e.IsDeleted).HasIndex(e => e.TransactionId);
        b.Entity<SplitLineEntity>().HasIndex(e => e.UpdatedAt);
        b.Entity<PayeeEntity>().HasQueryFilter(e => !e.IsDeleted).HasIndex(e => e.Name);
        b.Entity<PayeeEntity>().HasIndex(e => e.UpdatedAt);
        b.Entity<ExpectedTransactionEntity>().HasQueryFilter(e => !e.IsDeleted).HasIndex(e => e.AccountId);
        b.Entity<ExpectedTransactionEntity>().HasIndex(e => e.UpdatedAt);
        b.Entity<SettingEntity>().HasQueryFilter(e => !e.IsDeleted).HasIndex(e => e.UpdatedAt);
        b.Entity<SyncStateEntity>().HasKey(e => e.Key);
    }

    /// <summary>The history behind Ctrl+Z: what each save changed, so it can be reversed.</summary>
    public UndoLog Undo { get; } = new();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var rows = StampChanges();
        var saved = base.SaveChanges(acceptAllChangesOnSuccess);
        Undo.Record(rows);
        return saved;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var rows = StampChanges();
        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        Undo.Record(rows);
        return saved;
    }

    /// <summary>Turns deletes into tombstones and stamps every added or modified row with a new clock value.</summary>
    private List<UndoRow> StampChanges()
    {
        foreach (var entry in ChangeTracker.Entries<SyncEntity>().Where(e => e.State == EntityState.Deleted).ToList())
        {
            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;

            // A transaction's split lines go with it.
            if (entry.Entity is TransactionEntity tx)
            {
                foreach (var line in SplitLines.IgnoreQueryFilters().Where(s => s.TransactionId == tx.Id && !s.IsDeleted))
                    line.IsDeleted = true;
            }
        }

        ChangeTracker.DetectChanges();
        var rows = new List<UndoRow>();
        foreach (var entry in ChangeTracker.Entries<SyncEntity>()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
        {
            // Settings are not user data to undo (they include what the app remembers about itself).
            if (Undo.Recording && entry.Entity is not SettingEntity)
            {
                var added = entry.State == EntityState.Added;
                rows.Add(new UndoRow(entry.Entity.GetType(), entry.Entity.Id, added, entry.Entity.IsDeleted,
                    added ? new Dictionary<string, object?>() : entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue)));
            }

            entry.Entity.UpdatedAt = _clock.Next().ToString();
            entry.Entity.UpdatedBy = _clock.Node;
        }
        return rows;
    }
}

/// <summary>Local-only bookkeeping such as the last push and pull positions. Not synced.</summary>
public class SyncStateEntity
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>Lets "dotnet ef" create migrations without running the app.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<SpeedBudgetDb>
{
    public SpeedBudgetDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SpeedBudgetDb>().UseSqlite("Data Source=design.db").Options,
            new HlcClock("design"));
}
