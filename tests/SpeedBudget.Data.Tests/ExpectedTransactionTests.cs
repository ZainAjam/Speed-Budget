using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SpeedBudget.Core;
using SpeedBudget.Data;

namespace SpeedBudget.Data.Tests;

/// <summary>Expected transactions live in the transactions table: dated in the future they are scheduled, with no date they are undated. Neither has happened yet.</summary>
public sealed class ExpectedTransactionTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 17);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SpeedBudgetDb _db;
    private readonly BudgetStore _store;

    public ExpectedTransactionTests()
    {
        _connection.Open();
        _db = new SpeedBudgetDb(new DbContextOptionsBuilder<SpeedBudgetDb>().UseSqlite(_connection).Options, new HlcClock("devicea", () => 1_800_000_000_000));
        _db.Database.Migrate();
        _store = new BudgetStore(_db);
    }

    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    private async Task<(AccountEntity Account, ItemEntity Item)> Setup()
    {
        var account = new AccountEntity { Name = "Cheque", IsOnBudget = true, OpeningBalance = 1_000_00 };
        var item = new ItemEntity { Name = "Food", Kind = ItemKind.Expense, GroupId = Guid.NewGuid() };
        _db.Accounts.Add(account);
        _db.Items.Add(item);
        await _db.SaveChangesAsync();
        return (account, item);
    }

    private Task Add(AccountEntity account, ItemEntity item, DateOnly date, long amount, bool undated = false) =>
        _store.AddTransactionAsync(new TransactionEntity { AccountId = account.Id, Date = undated ? TransactionEntity.UndatedPlace : date, IsUndated = undated, Amount = amount, Payee = "Shop" },
            new[] { new SplitLineEntity { BudgetItemId = item.Id, Amount = amount } });

    [Fact]
    public void A_transaction_is_expected_when_it_has_no_date_or_a_future_date()
    {
        Assert.False(new TransactionEntity { Date = Today }.IsExpected(Today));
        Assert.False(new TransactionEntity { Date = Today.AddDays(-3) }.IsExpected(Today));
        Assert.True(new TransactionEntity { Date = Today.AddDays(1) }.IsExpected(Today));
        Assert.True(new TransactionEntity { Date = TransactionEntity.UndatedPlace, IsUndated = true }.IsExpected(Today));
    }

    [Fact]
    public async Task Scheduled_and_undated_transactions_change_no_balance_until_their_date_arrives()
    {
        var (account, item) = await Setup();
        await Add(account, item, Today.AddDays(-2), -100_00);
        await Add(account, item, Today.AddDays(5), -300_00);    // scheduled
        await Add(account, item, Today, -50_00, undated: true);  // expected, no date

        Assert.Equal(900_00, (await _store.GetBalancesAsync(account.Id, default, Today)).BookBalance);
        // On the day the scheduled one is due, it counts like any other.
        Assert.Equal(600_00, (await _store.GetBalancesAsync(account.Id, default, Today.AddDays(5))).BookBalance);
    }

    [Fact]
    public async Task Scheduled_and_undated_transactions_are_not_in_the_budget_until_they_happen()
    {
        var (account, item) = await Setup();
        await Add(account, item, Today.AddDays(-2), -100_00);
        await Add(account, item, Today.AddDays(5), -300_00);
        await Add(account, item, Today, -50_00, undated: true);

        var before = await _store.LoadSnapshotAsync(default, Today);
        Assert.Single(before.Transactions);
        var after = await _store.LoadSnapshotAsync(default, Today.AddDays(5));
        Assert.Equal(2, after.Transactions.Count);
    }

    [Fact]
    public async Task Giving_an_undated_transaction_a_date_makes_it_an_ordinary_one()
    {
        var (account, item) = await Setup();
        await Add(account, item, Today, -50_00, undated: true);
        var transaction = await _db.Transactions.SingleAsync();
        transaction.IsUndated = false;
        transaction.Date = Today;
        await _db.SaveChangesAsync();
        Assert.Equal(950_00, (await _store.GetBalancesAsync(account.Id, default, Today)).BookBalance);
    }

    [Fact]
    public async Task The_old_expected_list_moves_into_the_table_once()
    {
        var (account, item) = await Setup();
        await _store.AddExpectedAsync(new ExpectedTransactionEntity { AccountId = account.Id, Payee = "Tyres", Amount = -400_00, BudgetItemId = item.Id, Kind = ExpectedKind.Someday });
        await _store.AddExpectedAsync(new ExpectedTransactionEntity { AccountId = account.Id, Payee = "Dentist", Amount = -80_00, BudgetItemId = item.Id, Kind = ExpectedKind.Once, ExpectedDate = Today.AddDays(20) });
        await _store.AddExpectedAsync(new ExpectedTransactionEntity { AccountId = account.Id, Payee = "Rent", Amount = -900_00, Kind = ExpectedKind.Repeating, ExpectedDate = Today.AddDays(10), Repeat = ExpectedRepeat.Monthly, AnchorDay = 1 });

        Assert.Equal(3, await _store.MoveExpectedIntoTransactionsAsync());
        Assert.Equal(0, await _store.MoveExpectedIntoTransactionsAsync());   // only once

        var moved = await _db.Transactions.OrderBy(t => t.Payee).ToListAsync();
        Assert.Equal(3, moved.Count);
        Assert.True(moved.Single(t => t.Payee == "Tyres").IsUndated);
        Assert.Equal(Today.AddDays(20), moved.Single(t => t.Payee == "Dentist").Date);
        Assert.Equal(Today.AddDays(10), moved.Single(t => t.Payee == "Rent").Date);
        Assert.Empty(await _db.Expected.ToListAsync());
        Assert.Equal(1_000_00, (await _store.GetBalancesAsync(account.Id, default, Today)).BookBalance);   // none of them has happened
    }
}
