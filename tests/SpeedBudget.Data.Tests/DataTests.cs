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

    private Task<TransactionEntity> AddTx(AccountEntity account, DateOnly date, long amount, Guid? itemId = null, string payee = "") =>
        _store.AddTransactionAsync(
            new TransactionEntity { AccountId = account.Id, Date = date, DayOrder = 0, Amount = amount, Payee = payee },
            new[] { new SplitLineEntity { BudgetItemId = itemId, Amount = amount } });

    [Fact]
    public async Task Transactions_come_back_oldest_first_and_can_be_limited_to_one_account()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true);
        var b = await _store.AddAccountAsync("B", AccountType.Cheque, true);
        await AddTx(a, new(2027, 1, 9), -1_00, payee: "late");
        await AddTx(b, new(2027, 1, 2), -1_00, payee: "other");
        await AddTx(a, new(2027, 1, 3), -1_00, payee: "early");

        var all = await _store.GetTransactionsAsync();
        var onlyA = await _store.GetTransactionsAsync(a.Id);

        Assert.Equal(new[] { "other", "early", "late" }, all.Select(t => t.Transaction.Payee));
        Assert.Equal(new[] { "early", "late" }, onlyA.Select(t => t.Transaction.Payee));
    }

    [Fact]
    public async Task New_rows_on_a_date_go_after_existing_rows_and_markers_on_that_date()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true);
        var day = new DateOnly(2027, 1, 25);
        Assert.Equal(0, await _store.NextDayOrderAsync(day));

        var tx = await AddTx(a, day, -1_00);
        _db.Markers.Add(new MonthMarkerEntity { Date = day, DayOrder = 4, Label = "Feb" });
        await _db.SaveChangesAsync();

        Assert.Equal(0, tx.DayOrder);
        Assert.Equal(5, await _store.NextDayOrderAsync(day));
    }

    [Fact]
    public async Task Changing_a_transactions_item_and_amount_rewrites_its_single_split()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true);
        var food = AddItem("Food", ItemKind.Expense);
        var petrol = AddItem("Petrol", ItemKind.Expense);
        var tx = await AddTx(a, new(2027, 1, 3), -100_00, food.Id);

        tx.Amount = -150_00;
        await _store.SetSingleSplitAsync(tx.Id, petrol.Id, tx.Amount);

        var line = Assert.Single((await _store.GetTransactionsAsync()).Single().Lines);
        Assert.Equal(petrol.Id, line.BudgetItemId);
        Assert.Equal(-150_00, line.Amount);
    }

    [Fact]
    public async Task Payee_hints_use_the_most_recent_item_and_ignore_transfers()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true);
        var s = await _store.AddAccountAsync("S", AccountType.Savings, false);
        var food = AddItem("Food", ItemKind.Expense);
        var petrol = AddItem("Petrol", ItemKind.Expense);
        await AddTx(a, new(2027, 1, 3), -10_00, food.Id, "Polo");
        await AddTx(a, new(2027, 1, 9), -10_00, petrol.Id, "polo");
        await _store.AddTransferAsync(a.Id, s.Id, new(2027, 1, 10), 5_00, null, petrol.Id);

        var hint = Assert.Single(await _store.GetPayeeHintsAsync());

        Assert.Equal("polo", hint.Payee);
        Assert.Equal(petrol.Id, hint.BudgetItemId);
    }

    [Fact]
    public async Task Transfer_between_two_budget_accounts_does_not_touch_the_budget()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true);
        var b = await _store.AddAccountAsync("B", AccountType.Cheque, true);
        var food = AddItem("Food", ItemKind.Expense);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, food.Id, 100_00);

        var (from, to) = await _store.AddTransferAsync(a.Id, b.Id, new(2027, 1, 5), 40_00, "move", null);

        Assert.Equal(-40_00, from.Amount);
        Assert.Equal(40_00, to.Amount);
        Assert.Equal(from.TransferPairId, to.TransferPairId);
        var month = BudgetEngine.Calculate(await _store.LoadSnapshotAsync()).Single();
        Assert.Equal(100_00, month.Items.Single(i => i.ItemId == food.Id).Remaining);
        Assert.Equal(0, month.Income);
    }

    [Fact]
    public async Task Transfer_from_budget_to_tracked_counts_as_spending_on_the_chosen_item()
    {
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        var pocket = await _store.AddAccountAsync("Pocket", AccountType.Savings, false);
        var saving = AddItem("Savings", ItemKind.Expense);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, saving.Id, 500_00);

        await _store.AddTransferAsync(cheque.Id, pocket.Id, new(2027, 1, 5), 200_00, null, saving.Id);

        var item = BudgetEngine.Calculate(await _store.LoadSnapshotAsync()).Single().Items.Single(i => i.ItemId == saving.Id);
        Assert.Equal(-200_00, item.Activity);
        Assert.Equal(300_00, item.Remaining);
    }

    [Fact]
    public async Task Transfer_from_tracked_to_budget_counts_as_income_and_needs_an_income_item()
    {
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        var pocket = await _store.AddAccountAsync("Pocket", AccountType.Savings, false);
        var salary = AddItem("Salary", ItemKind.Income);
        var food = AddItem("Food", ItemKind.Expense);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.AddTransferAsync(pocket.Id, cheque.Id, new(2027, 1, 5), 100_00, null, null));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.AddTransferAsync(pocket.Id, cheque.Id, new(2027, 1, 5), 100_00, null, food.Id));

        await _store.AddTransferAsync(pocket.Id, cheque.Id, new(2027, 1, 5), 100_00, null, salary.Id);
        Assert.Equal(100_00, BudgetEngine.Calculate(await _store.LoadSnapshotAsync()).Single().Income);
    }

    [Fact]
    public async Task Invalid_transfers_are_rejected()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true);
        var b = await _store.AddAccountAsync("B", AccountType.Cheque, true);

        await Assert.ThrowsAsync<ArgumentException>(() => _store.AddTransferAsync(a.Id, a.Id, new(2027, 1, 5), 10_00, null, null));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.AddTransferAsync(a.Id, b.Id, new(2027, 1, 5), 0, null, null));
    }

    [Fact]
    public async Task Deleting_one_side_of_a_transfer_deletes_both()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true);
        var b = await _store.AddAccountAsync("B", AccountType.Cheque, true);
        var (from, _) = await _store.AddTransferAsync(a.Id, b.Id, new(2027, 1, 5), 10_00, null, null);

        await _store.DeleteTransactionAsync(from);

        Assert.Empty(await _store.GetTransactionsAsync());
        Assert.Empty(_db.SplitLines.ToList());
    }

    [Fact]
    public async Task First_month_label_defaults_then_can_be_changed_without_duplicating_the_row()
    {
        Assert.Equal(BudgetStore.DefaultFirstMonthLabel, await _store.GetFirstMonthLabelAsync());

        await _store.SetFirstMonthLabelAsync("March 2027");
        await _store.SetFirstMonthLabelAsync("April 2027");

        Assert.Equal("April 2027", await _store.GetFirstMonthLabelAsync());
        Assert.Single(_db.Settings.ToList());
        Assert.Equal(SettingEntity.IdFor("firstMonthLabel"), _db.Settings.Single().Id);
        Assert.Equal("April 2027", (await _store.LoadSnapshotAsync()).FirstMonthLabel);
    }

    [Fact]
    public async Task Adding_a_marker_puts_later_entries_in_the_new_month_and_copies_the_budget()
    {
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        var food = AddItem("Food", ItemKind.Expense);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, food.Id, 800_00);
        await AddTx(cheque, new(2027, 1, 20), -100_00, food.Id);

        var marker = await _store.AddMarkerAsync("February 2027", new(2027, 1, 25), copyPreviousBudget: true);
        await AddTx(cheque, new(2027, 1, 25), -30_00, food.Id);   // same day, entered after the line
        await AddTx(cheque, new(2027, 1, 26), -20_00, food.Id);

        var months = BudgetEngine.Calculate(await _store.LoadSnapshotAsync());

        Assert.Equal(2, months.Count);
        Assert.Equal(marker.Id, months[1].MonthId);
        Assert.Equal(800_00, months[1].Items.Single(i => i.ItemId == food.Id).Budgeted);
        Assert.Equal(-50_00, months[1].Items.Single(i => i.ItemId == food.Id).Activity);
        Assert.Equal(-100_00, months[0].Items.Single(i => i.ItemId == food.Id).Activity);
    }

    [Fact]
    public async Task Copying_a_budget_does_not_overwrite_amounts_already_set()
    {
        var food = AddItem("Food", ItemKind.Expense);
        var petrol = AddItem("Petrol", ItemKind.Expense);
        var marker = await _store.AddMarkerAsync("Feb", new(2027, 2, 1), copyPreviousBudget: false);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, food.Id, 500_00);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, petrol.Id, 300_00);
        await _store.SetAllocationAsync(marker.Id, food.Id, 999_00);

        await _store.CopyBudgetAsync(BudgetEngine.FirstMonthId, marker.Id);

        var feb = _db.Allocations.Where(a => a.MonthId == marker.Id).ToList();
        Assert.Equal(999_00, feb.Single(a => a.BudgetItemId == food.Id).Amount);
        Assert.Equal(300_00, feb.Single(a => a.BudgetItemId == petrol.Id).Amount);
    }

    [Fact]
    public async Task Moving_a_marker_moves_rows_between_months_and_refuses_to_pass_another_line()
    {
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        var food = AddItem("Food", ItemKind.Expense);
        await AddTx(cheque, new(2027, 1, 10), -10_00, food.Id);
        await AddTx(cheque, new(2027, 1, 20), -20_00, food.Id);
        var feb = await _store.AddMarkerAsync("Feb", new(2027, 1, 25), false);
        var mar = await _store.AddMarkerAsync("Mar", new(2027, 2, 25), false);

        Assert.True(await _store.MoveMarkerAsync(feb, new SortKey(new(2027, 1, 15), 0)));
        var months = BudgetEngine.Calculate(await _store.LoadSnapshotAsync());
        Assert.Equal(-10_00, months[0].Items.Single(i => i.ItemId == food.Id).Activity);
        Assert.Equal(-20_00, months[1].Items.Single(i => i.ItemId == food.Id).Activity);

        Assert.False(await _store.MoveMarkerAsync(feb, new SortKey(new(2027, 3, 1), 0)));
        Assert.Equal(new DateOnly(2027, 1, 15), feb.Date);
    }

    [Fact]
    public async Task Deleting_a_marker_merges_its_month_into_the_previous_one()
    {
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        var food = AddItem("Food", ItemKind.Expense);
        var petrol = AddItem("Petrol", ItemKind.Expense);
        var salary = AddItem("Salary", ItemKind.Income);
        var feb = await _store.AddMarkerAsync("Feb", new(2027, 1, 25), false);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, food.Id, 500_00);
        await _store.SetAllocationAsync(feb.Id, food.Id, 400_00);
        await _store.SetAllocationAsync(feb.Id, petrol.Id, 100_00);
        await _store.SetIncomeHoldAsync(feb.Id, salary.Id, 50_00);
        await AddTx(cheque, new(2027, 1, 26), -60_00, food.Id);

        await _store.DeleteMarkerAsync(feb);

        var months = BudgetEngine.Calculate(await _store.LoadSnapshotAsync());
        var merged = Assert.Single(months);
        Assert.Equal(900_00, merged.Items.Single(i => i.ItemId == food.Id).Budgeted);
        Assert.Equal(100_00, merged.Items.Single(i => i.ItemId == petrol.Id).Budgeted);
        Assert.Equal(-60_00, merged.Items.Single(i => i.ItemId == food.Id).Activity);
        Assert.Equal(50_00, merged.HeldOut);
        Assert.Empty(await _store.GetMarkersAsync());
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
