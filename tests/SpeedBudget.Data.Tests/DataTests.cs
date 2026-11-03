using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SpeedBudget.Core;
using SpeedBudget.Data;

namespace SpeedBudget.Data.Tests;

public sealed class DataTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SpeedBudgetDb _db;
    private readonly BudgetStore _store;
    private long _wall = 1_800_000_000_000;

    public DataTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<SpeedBudgetDb>().UseSqlite(_connection).Options;
        _db = new SpeedBudgetDb(options, new HlcClock("devicea", () => _wall));
        _db.Database.Migrate();
        _store = new BudgetStore(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private AccountEntity AddAccount(string name, bool onBudget)
    {
        var a = new AccountEntity { Name = name, IsOnBudget = onBudget };
        _db.Accounts.Add(a);
        _db.SaveChanges();
        return a;
    }

    private ItemEntity AddItem(string name, ItemKind kind)
    {
        var i = new ItemEntity { Name = name, Kind = kind, GroupId = Guid.NewGuid() };
        _db.Items.Add(i);
        _db.SaveChanges();
        return i;
    }

    [Fact]
    public void Clock_never_goes_backwards_even_if_the_wall_clock_does()
    {
        var clock = new HlcClock("n", () => _wall);
        var a = clock.Next();
        _wall -= 5_000;
        var b = clock.Next();
        var c = clock.Next();

        Assert.True(string.CompareOrdinal(a.ToString(), b.ToString()) < 0);
        Assert.True(string.CompareOrdinal(b.ToString(), c.ToString()) < 0);
    }

    [Fact]
    public void Receiving_a_remote_timestamp_makes_later_local_edits_sort_after_it()
    {
        var clock = new HlcClock("n", () => _wall);
        var remote = new Hlc(_wall + 60_000, 7, "other");

        clock.Receive(remote);
        var next = clock.Next();

        Assert.True(string.CompareOrdinal(remote.ToString(), next.ToString()) < 0);
        Assert.Equal(remote, Hlc.Parse(remote.ToString()));
    }

    [Fact]
    public void Saving_stamps_rows_with_the_device_and_a_clock_value()
    {
        var account = AddAccount("Cheque", true);

        Assert.Equal("devicea", account.UpdatedBy);
        Assert.NotEqual("", account.UpdatedAt);
    }

    [Fact]
    public void Editing_a_row_moves_its_stamp_forward()
    {
        var account = AddAccount("Cheque", true);
        var before = account.UpdatedAt;

        account.Name = "Easy Account";
        _db.SaveChanges();

        Assert.True(string.CompareOrdinal(before, account.UpdatedAt) < 0);
    }

    [Fact]
    public void Deleting_leaves_a_tombstone_that_is_hidden_but_still_syncs()
    {
        var account = AddAccount("Old", true);

        _db.Accounts.Remove(account);
        _db.SaveChanges();

        Assert.Empty(_db.Accounts.ToList());
        var tombstone = _db.Accounts.IgnoreQueryFilters().Single();
        Assert.True(tombstone.IsDeleted);
    }

    [Fact]
    public async Task Deleting_a_transaction_also_tombstones_its_split_lines()
    {
        var account = AddAccount("Cheque", true);
        var food = AddItem("Food", ItemKind.Expense);
        var tx = await _store.AddTransactionAsync(
            new TransactionEntity { AccountId = account.Id, Date = new(2027, 1, 5), Amount = -100_00 },
            new[] { new SplitLineEntity { BudgetItemId = food.Id, Amount = -100_00 } });

        _db.Transactions.Remove(tx);
        await _db.SaveChangesAsync();

        Assert.Empty(_db.SplitLines.ToList());
        Assert.True(_db.SplitLines.IgnoreQueryFilters().Single().IsDeleted);
    }

    [Fact]
    public async Task Unbalanced_splits_are_rejected()
    {
        var account = AddAccount("Cheque", true);

        await Assert.ThrowsAsync<ArgumentException>(() => _store.AddTransactionAsync(
            new TransactionEntity { AccountId = account.Id, Date = new(2027, 1, 5), Amount = -100_00 },
            new[] { new SplitLineEntity { Amount = -60_00 } }));
    }

    [Fact]
    public async Task Changes_after_a_cursor_include_only_newer_rows_and_tombstones()
    {
        var keep = AddAccount("Keep", true);
        var cursor = keep.UpdatedAt;
        var gone = AddAccount("Gone", true);
        _db.Accounts.Remove(gone);
        _db.SaveChanges();

        var changes = await _store.GetChangesAfterAsync(cursor);

        var only = Assert.Single(changes);
        Assert.Equal(gone.Id, only.Id);
        Assert.True(only.IsDeleted);
    }

    [Fact]
    public async Task Allocation_and_hold_are_upserted_not_duplicated()
    {
        var food = AddItem("Food", ItemKind.Expense);
        var salary = AddItem("Salary", ItemKind.Income);

        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, food.Id, 500_00);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, food.Id, 700_00);
        await _store.SetIncomeHoldAsync(BudgetEngine.FirstMonthId, salary.Id, 100_00);
        await _store.SetIncomeHoldAsync(BudgetEngine.FirstMonthId, salary.Id, 0);

        Assert.Equal(700_00, Assert.Single(_db.Allocations.ToList()).Amount);
        Assert.Empty(_db.Holds.ToList());
    }

    [Fact]
    public async Task Fresh_database_is_empty_until_something_is_added()
    {
        Assert.True(await _store.IsEmptyAsync());
        await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        Assert.False(await _store.IsEmptyAsync());
    }

    [Fact]
    public async Task Starter_items_create_income_and_expense_groups_with_matching_item_kinds()
    {
        await _store.SeedStarterItemsAsync();

        var groups = await _store.GetGroupsAsync();
        var items = await _store.GetItemsAsync();

        Assert.Equal(StarterItems.Groups.Count, groups.Count);
        Assert.Equal(StarterItems.Groups.Sum(g => g.Items.Length), items.Count);
        var income = groups.Single(g => g.Kind == ItemKind.Income);
        Assert.All(items.Where(i => i.GroupId == income.Id), i => Assert.Equal(ItemKind.Income, i.Kind));
    }

    [Fact]
    public async Task Account_with_transactions_is_closed_not_deleted()
    {
        var used = await _store.AddAccountAsync("Used", AccountType.Cheque, true);
        var unused = await _store.AddAccountAsync("Unused", AccountType.Cash, false);
        await _store.AddTransactionAsync(
            new TransactionEntity { AccountId = used.Id, Date = new(2027, 1, 5), Amount = 10_00 },
            new[] { new SplitLineEntity { Amount = 10_00 } });

        Assert.False(await _store.DeleteOrCloseAccountAsync(used));
        Assert.True(await _store.DeleteOrCloseAccountAsync(unused));

        var left = await _store.GetAccountsAsync();
        Assert.Equal("Used", Assert.Single(left).Name);
        Assert.True(left[0].IsClosed);
    }

    [Fact]
    public async Task Item_in_use_or_with_sub_items_is_archived_not_deleted()
    {
        var group = await _store.AddGroupAsync("Daily", ItemKind.Expense);
        var parent = await _store.AddItemAsync(group.Id, null, "Parent", ItemKind.Expense);
        await _store.AddItemAsync(group.Id, parent.Id, "Child", ItemKind.Expense);
        var plain = await _store.AddItemAsync(group.Id, null, "Plain", ItemKind.Expense);

        Assert.False(await _store.DeleteOrArchiveItemAsync(parent));
        Assert.True(await _store.DeleteOrArchiveItemAsync(plain));
        Assert.True(parent.IsArchived);
        Assert.False(await _store.DeleteGroupAsync(group));
    }

    [Fact]
    public async Task Moving_an_item_swaps_it_with_its_neighbour_and_stops_at_the_ends()
    {
        var group = await _store.AddGroupAsync("Daily", ItemKind.Expense);
        var a = await _store.AddItemAsync(group.Id, null, "A", ItemKind.Expense);
        var b = await _store.AddItemAsync(group.Id, null, "B", ItemKind.Expense);
        var c = await _store.AddItemAsync(group.Id, null, "C", ItemKind.Expense);

        Assert.False(await _store.MoveItemAsync(a, up: true));
        Assert.True(await _store.MoveItemAsync(c, up: true));

        var order = (await _store.GetItemsAsync()).Select(i => i.Name).ToArray();
        Assert.Equal(new[] { "A", "C", "B" }, order);
    }

    [Fact]
    public async Task Stored_data_feeds_the_budget_engine()
    {
        var cheque = AddAccount("Cheque", true);
        var savings = AddAccount("Savings", false);
        var salary = AddItem("Salary", ItemKind.Income);
        var food = AddItem("Food", ItemKind.Expense);

        var marker = new MonthMarkerEntity { Date = new(2027, 2, 1), DayOrder = 0, Label = "February 2027" };
        _db.Markers.Add(marker);
        await _db.SaveChangesAsync();

        await _store.AddTransactionAsync(
            new TransactionEntity { AccountId = cheque.Id, Date = new(2027, 1, 1), Amount = 5_000_00 },
            new[] { new SplitLineEntity { BudgetItemId = salary.Id, Amount = 5_000_00 } });
        await _store.AddTransactionAsync(
            new TransactionEntity { AccountId = cheque.Id, Date = new(2027, 1, 10), Amount = -200_00 },
            new[] { new SplitLineEntity { BudgetItemId = food.Id, Amount = -200_00 } });
        await _store.AddTransactionAsync(
            new TransactionEntity { AccountId = savings.Id, Date = new(2027, 1, 11), Amount = 9_999_00 },
            new[] { new SplitLineEntity { BudgetItemId = salary.Id, Amount = 9_999_00 } });
        await _store.AddTransactionAsync(
            new TransactionEntity { AccountId = cheque.Id, Date = new(2027, 2, 3), Amount = -50_00 },
            new[] { new SplitLineEntity { BudgetItemId = food.Id, Amount = -50_00 } });
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, food.Id, 1_000_00);

        var months = BudgetEngine.Calculate(await _store.LoadSnapshotAsync());

        Assert.Equal(2, months.Count);
        Assert.Equal(5_000_00, months[0].Income);
        Assert.Equal(800_00, months[0].Items.Single(i => i.ItemId == food.Id).Remaining);
        Assert.Equal(-50_00, months[1].Items.Single(i => i.ItemId == food.Id).Activity);
    }
}
