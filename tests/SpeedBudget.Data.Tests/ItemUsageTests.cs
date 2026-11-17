using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SpeedBudget.Core;
using SpeedBudget.Data;

namespace SpeedBudget.Data.Tests;

public sealed class ItemUsageTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SpeedBudgetDb _db;
    private readonly BudgetStore _store;

    public ItemUsageTests()
    {
        _connection.Open();
        _db = new SpeedBudgetDb(new DbContextOptionsBuilder<SpeedBudgetDb>().UseSqlite(_connection).Options, new HlcClock("devicea", () => 1_800_000_000_000));
        _db.Database.Migrate();
        _store = new BudgetStore(_db);
    }

    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    [Fact]
    public async Task Items_are_counted_by_the_budget_account_transactions_that_use_them()
    {
        var cheque = new AccountEntity { Name = "Cheque", IsOnBudget = true };
        var retirement = new AccountEntity { Name = "Retirement", IsOnBudget = false };
        var food = new ItemEntity { Name = "Food", Kind = ItemKind.Expense, GroupId = Guid.NewGuid() };
        var fuel = new ItemEntity { Name = "Fuel", Kind = ItemKind.Expense, GroupId = Guid.NewGuid() };
        _db.Accounts.AddRange(cheque, retirement);
        _db.Items.AddRange(food, fuel);
        await _db.SaveChangesAsync();

        for (var i = 0; i < 3; i++)
            await _store.AddTransactionAsync(new TransactionEntity { AccountId = cheque.Id, Date = new(2027, 1, 1 + i), Amount = -10_00 },
                new[] { new SplitLineEntity { BudgetItemId = food.Id, Amount = -10_00 } });
        // A split over two items counts once for each; a tracking account's transaction counts for nothing.
        await _store.AddTransactionAsync(new TransactionEntity { AccountId = cheque.Id, Date = new(2027, 1, 9), Amount = -30_00 },
            new[] { new SplitLineEntity { BudgetItemId = food.Id, Amount = -10_00 }, new SplitLineEntity { BudgetItemId = fuel.Id, Amount = -20_00 } });
        await _store.AddTransactionAsync(new TransactionEntity { AccountId = retirement.Id, Date = new(2027, 1, 10), Amount = -5_00 },
            new[] { new SplitLineEntity { BudgetItemId = fuel.Id, Amount = -5_00 } });

        var usage = await _store.GetItemUsageAsync();
        Assert.Equal(4, usage[food.Id]);
        Assert.Equal(1, usage[fuel.Id]);
    }
}
