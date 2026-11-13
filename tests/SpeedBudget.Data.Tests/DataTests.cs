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
    public async Task Household_template_creates_income_and_expense_groups_with_matching_item_kinds()
    {
        var plan = SetupTemplates.Create("Household");
        await _store.ApplySetupAsync(plan);

        var groups = await _store.GetGroupsAsync();
        var items = await _store.GetItemsAsync();

        Assert.Equal(plan.Groups.Count, groups.Count);
        Assert.Equal(plan.Groups.Sum(g => g.Items.Count), items.Count);
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
    public async Task An_item_that_has_been_used_or_has_sub_items_cannot_be_deleted_and_is_left_alone()
    {
        var group = await _store.AddGroupAsync("Daily", ItemKind.Expense);
        var parent = await _store.AddItemAsync(group.Id, null, "Parent", ItemKind.Expense);
        await _store.AddItemAsync(group.Id, parent.Id, "Child", ItemKind.Expense);
        var plain = await _store.AddItemAsync(group.Id, null, "Plain", ItemKind.Expense);
        var budgeted = await _store.AddItemAsync(group.Id, null, "Budgeted", ItemKind.Expense);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, budgeted.Id, 100_00);

        Assert.False(await _store.DeleteItemAsync(parent));
        Assert.False(await _store.DeleteItemAsync(budgeted));
        Assert.True(await _store.DeleteItemAsync(plain));
        Assert.Equal("it has sub-items", await _store.WhyItemCannotBeDeletedAsync(parent.Id));
        Assert.Equal("it has budget on it", await _store.WhyItemCannotBeDeletedAsync(budgeted.Id));
        Assert.False(await _store.DeleteGroupAsync(group));
        Assert.Equal(3, (await _store.GetItemsAsync()).Count);   // the ones that could not be deleted are all still there
    }

    [Fact]
    public async Task An_item_whose_budget_was_typed_and_cleared_back_to_zero_can_still_be_deleted()
    {
        var group = await _store.AddGroupAsync("Daily", ItemKind.Expense);
        var item = await _store.AddItemAsync(group.Id, null, "Food", ItemKind.Expense);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, item.Id, 100_00);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, item.Id, 0);

        Assert.Null(await _store.WhyItemCannotBeDeletedAsync(item.Id));
        Assert.True(await _store.DeleteItemAsync(item));
        Assert.Empty(await _store.GetItemsAsync());
    }

    [Fact]
    public async Task Items_hidden_by_an_earlier_version_are_shown_again()
    {
        var group = await _store.AddGroupAsync("Daily", ItemKind.Expense);
        var item = await _store.AddItemAsync(group.Id, null, "Old", ItemKind.Expense);
        item.IsArchived = true;
        await _store.SaveAsync();

        Assert.Equal(1, await _store.ShowAllItemsAsync());
        Assert.False((await _store.GetItemsAsync()).Single().IsArchived);
        Assert.Equal(0, await _store.ShowAllItemsAsync());
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

        await _store.AddTransferAsync(pocket.Id, cheque.Id, new(2027, 1, 5), 50_00, null, null);   // no item is fine: it just counts for nothing yet
        Assert.Equal(0, BudgetEngine.Calculate(await _store.LoadSnapshotAsync()).Single().Income);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.AddTransferAsync(pocket.Id, cheque.Id, new(2027, 1, 5), 100_00, null, food.Id));

        await _store.AddTransferAsync(pocket.Id, cheque.Id, new(2027, 1, 5), 100_00, null, salary.Id);
        Assert.Equal(100_00, BudgetEngine.Calculate(await _store.LoadSnapshotAsync()).Single().Income);
    }

    [Fact]
    public async Task Editing_one_side_of_a_transfer_updates_both_sides()
    {
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        var pocket = await _store.AddAccountAsync("Pocket", AccountType.Savings, false);
        var other = await _store.AddAccountAsync("Other", AccountType.Savings, false);
        var saving = AddItem("Savings", ItemKind.Expense);
        var (from, to) = await _store.AddTransferAsync(cheque.Id, pocket.Id, new(2027, 1, 5), 200_00, null, saving.Id);

        from.Amount = -350_00;
        from.Date = new(2027, 1, 9);
        from.Memo = "top up";
        var note = await _store.UpdateTransferAsync(from.Id, other.Id, saving.Id);

        Assert.Null(note);
        Assert.Equal(350_00, to.Amount);
        Assert.Equal(new DateOnly(2027, 1, 9), to.Date);
        Assert.Equal("top up", to.Memo);
        Assert.Equal(other.Id, to.AccountId);
        Assert.Equal("Transfer to Other", from.Payee);
        Assert.Equal("Transfer from Cheque", to.Payee);
        var lines = await _store.GetSplitLinesAsync(from.Id);
        Assert.Equal(saving.Id, lines.Single().BudgetItemId);
        Assert.Equal(-350_00, lines.Single().Amount);
    }

    [Fact]
    public async Task A_refused_transfer_edit_is_undone()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true);
        var b = await _store.AddAccountAsync("B", AccountType.Cheque, true);
        var (from, _) = await _store.AddTransferAsync(a.Id, b.Id, new(2027, 1, 5), 40_00, null, null);

        from.Amount = -10_00;
        await Assert.ThrowsAsync<ArgumentException>(() => _store.UpdateTransferAsync(from.Id, a.Id, null));   // to its own account

        Assert.Equal(-40_00, from.Amount);
    }

    [Fact]
    public async Task Making_a_transfer_cross_the_budget_needs_no_item_and_reversing_it_flips_the_names()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true);
        var b = await _store.AddAccountAsync("B", AccountType.Cheque, true);
        var tracked = await _store.AddAccountAsync("T", AccountType.Savings, false);
        var (from, to) = await _store.AddTransferAsync(a.Id, b.Id, new(2027, 1, 5), 40_00, null, null);

        Assert.Null(await _store.UpdateTransferAsync(from.Id, tracked.Id, null));   // no item yet is allowed

        from.Amount = 40_00;   // the money now comes the other way
        await _store.UpdateTransferAsync(from.Id, null, null);
        Assert.Equal(-40_00, to.Amount);
        Assert.Equal("Transfer from T", from.Payee);
        Assert.Equal("Transfer to A", to.Payee);
    }

    [Fact]
    public async Task A_setting_can_be_set_again_after_it_was_removed()
    {
        await _store.SetSettingAsync("k", "1");
        await _store.SetSettingAsync("k", null);
        Assert.Null(await _store.GetSettingAsync("k"));

        await _store.SetSettingAsync("k", "2");
        Assert.Equal("2", await _store.GetSettingAsync("k"));
    }

    [Fact]
    public async Task The_first_month_can_be_chosen_after_it_was_set_by_a_transaction()
    {
        await _store.SetFirstMonthLabelAsync("March 2024");   // removes the "renamed" setting
        await _store.RenameFirstMonthAsync("November 2025");   // sets it again

        Assert.Equal("November 2025", await _store.GetFirstMonthLabelAsync());
    }

    [Fact]
    public async Task A_budget_without_a_start_date_gets_one_from_its_data_and_then_keeps_it()
    {
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        await AddTx(cheque, new(2025, 11, 20), -10_00);

        Assert.Equal(new DateOnly(2025, 11, 1), await _store.EnsureBudgetStartAsync());

        // An earlier transaction no longer pulls the first month back by itself: the start date is the truth.
        await AddTx(cheque, new(2025, 8, 3), -10_00);
        Assert.Equal("November 2025", await _store.GetFirstMonthLabelAsync());
    }

    [Fact]
    public async Task Moving_the_start_earlier_adds_a_line_for_every_month_and_keeps_the_old_first_month_intact()
    {
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        var food = AddItem("Food", ItemKind.Expense);
        await AddTx(cheque, new(2025, 11, 20), -50_00, food.Id);
        await _store.EnsureBudgetStartAsync();
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, food.Id, 800_00);

        Assert.Null(await _store.SetBudgetStartAsync(new(2025, 8, 15)));

        var snapshot = await _store.LoadSnapshotAsync();
        var months = BudgetEngine.Calculate(snapshot);
        Assert.Equal(new[] { "August 2025", "September 2025", "October 2025", "November 2025" }, months.Select(m => m.Label));
        var november = months.Single(m => m.Label == "November 2025");
        Assert.Equal(800_00, november.Items.Single(i => i.ItemId == food.Id).Budgeted);
        Assert.Equal(-50_00, november.Items.Single(i => i.ItemId == food.Id).Activity);
        Assert.Equal(0, months[0].Items.Sum(i => i.Budgeted + i.Activity));
        Assert.Equal(new DateOnly(2025, 8, 15), await _store.EnsureBudgetStartAsync());
    }

    [Fact]
    public async Task The_start_cannot_move_past_a_transaction_or_a_month_line_and_a_line_cannot_precede_it()
    {
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        await AddTx(cheque, new(2025, 11, 20), -10_00);
        await _store.EnsureBudgetStartAsync();
        await _store.AddMarkerAsync("December 2025", new(2025, 12, 1), copyPreviousBudget: false);

        Assert.NotNull(await _store.SetBudgetStartAsync(new(2025, 11, 25)));   // after the transaction
        Assert.NotNull(await _store.SetBudgetStartAsync(new(2025, 12, 5)));    // after the first line
        await Assert.ThrowsAsync<ArgumentException>(() => _store.AddMarkerAsync("October 2025", new(2025, 10, 1), copyPreviousBudget: false));

        // A later date that is still before everything is fine.
        Assert.Null(await _store.SetBudgetStartAsync(new(2025, 11, 10)));
        Assert.Equal(new DateOnly(2025, 11, 10), await _store.EnsureBudgetStartAsync());
    }

    [Fact]
    public async Task A_budget_can_start_on_a_date_in_one_month_and_count_as_the_start_of_the_next()
    {
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        var food = AddItem("Food", ItemKind.Expense);
        await _store.EnsureBudgetStartAsync();

        Assert.Null(await _store.SetBudgetStartAsync(new(2026, 10, 24), new(2026, 11, 1)));
        var tx = await AddTx(cheque, new(2026, 10, 28), -20_00, food.Id);   // dated in October, but after the line that starts November

        Assert.Equal(new DateOnly(2026, 10, 24), await _store.EnsureBudgetStartAsync());
        Assert.Equal("November 2026", await _store.GetFirstMonthLabelAsync());
        var months = BudgetEngine.Calculate(await _store.LoadSnapshotAsync());
        Assert.Equal(-20_00, months.Single(m => m.Label == "November 2026").Items.Single(i => i.ItemId == food.Id).Activity);

        // The next line has to be for a later month, and the first month has to come before it.
        await _store.AddMarkerAsync("December 2026", new(2026, 12, 1), copyPreviousBudget: false);
        Assert.NotNull(await _store.SetBudgetStartAsync(new(2026, 10, 24), new(2026, 12, 1)));
        Assert.Null(await _store.SetBudgetStartAsync(new(2026, 10, 24), new(2026, 9, 1)));
        Assert.Equal("September 2026", await _store.GetFirstMonthLabelAsync());
    }

    [Fact]
    public async Task A_transfer_can_have_nothing_in_it_and_keeps_its_direction()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true);
        var b = await _store.AddAccountAsync("B", AccountType.Cheque, true);
        var (from, to) = await _store.AddTransferAsync(a.Id, b.Id, new(2027, 1, 5), 0, null, null);
        Assert.Equal(0, from.Amount);
        Assert.Equal(0, to.Amount);

        from.Amount = -25_00;
        await _store.UpdateTransferAsync(from.Id, null, null);
        from.Amount = 0;
        await _store.UpdateTransferAsync(from.Id, null, null);   // back to nothing: each side keeps its role

        Assert.Equal(0, to.Amount);
        Assert.Equal("Transfer to B", from.Payee);
        Assert.Equal("Transfer from A", to.Payee);
    }

    [Fact]
    public async Task Invalid_transfers_are_rejected()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true);
        var b = await _store.AddAccountAsync("B", AccountType.Cheque, true);

        await Assert.ThrowsAsync<ArgumentException>(() => _store.AddTransferAsync(a.Id, a.Id, new(2027, 1, 5), 10_00, null, null));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.AddTransferAsync(a.Id, b.Id, new(2027, 1, 5), -1, null, null));
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
    public async Task Batch_allocation_update_creates_missing_rows_and_overwrites_existing_ones()
    {
        var food = AddItem("Food", ItemKind.Expense);
        var petrol = AddItem("Petrol", ItemKind.Expense);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, food.Id, 100_00);

        await _store.SetAllocationsAsync(BudgetEngine.FirstMonthId, new[] { (food.Id, 250_00L), (petrol.Id, 75_00L) });

        var rows = _db.Allocations.ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(250_00, rows.Single(a => a.BudgetItemId == food.Id).Amount);
        Assert.Equal(75_00, rows.Single(a => a.BudgetItemId == petrol.Id).Amount);
    }

    [Fact]
    public async Task Splitting_a_transaction_spreads_it_across_items_in_the_engine()
    {
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        var food = AddItem("Food", ItemKind.Expense);
        var petrol = AddItem("Petrol", ItemKind.Expense);
        var tx = await AddTx(cheque, new(2027, 1, 4), -500_00, food.Id, "Shoprite");

        await _store.SetSplitsAsync(tx.Id, new (Guid?, long, string?)[] { (food.Id, -350_00, null), (petrol.Id, -150_00, "fuel") });

        var detail = Assert.Single(await _store.GetTransactionsAsync());
        Assert.Equal(2, detail.Lines.Count);
        var month = BudgetEngine.Calculate(await _store.LoadSnapshotAsync()).Single();
        Assert.Equal(-350_00, month.Items.Single(i => i.ItemId == food.Id).Activity);
        Assert.Equal(-150_00, month.Items.Single(i => i.ItemId == petrol.Id).Activity);
    }

    [Fact]
    public async Task Splits_that_do_not_add_up_or_are_empty_are_rejected_and_leave_the_old_lines()
    {
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        var food = AddItem("Food", ItemKind.Expense);
        var tx = await AddTx(cheque, new(2027, 1, 4), -100_00, food.Id);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.SetSplitsAsync(tx.Id, new (Guid?, long, string?)[] { (food.Id, -60_00, null) }));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SetSplitsAsync(tx.Id, Array.Empty<(Guid?, long, string?)>()));

        Assert.Equal(-100_00, Assert.Single((await _store.GetTransactionsAsync()).Single().Lines).Amount);
    }

    [Fact]
    public async Task Statement_balance_is_remembered_per_account_and_can_be_cleared()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true);
        var b = await _store.AddAccountAsync("B", AccountType.Cheque, true);

        Assert.Null(await _store.GetStatementBalanceAsync(a.Id));
        await _store.SetStatementBalanceAsync(a.Id, 9_054_75);
        await _store.SetStatementBalanceAsync(a.Id, 9_100_00);

        Assert.Equal(9_100_00, await _store.GetStatementBalanceAsync(a.Id));
        Assert.Null(await _store.GetStatementBalanceAsync(b.Id));

        await _store.SetStatementBalanceAsync(a.Id, null);
        Assert.Null(await _store.GetStatementBalanceAsync(a.Id));
    }

    [Fact]
    public async Task An_item_moves_to_another_group_at_the_chosen_place_taking_its_sub_items_along()
    {
        var home = await _store.AddGroupAsync("Home", ItemKind.Expense);
        var daily = await _store.AddGroupAsync("Daily", ItemKind.Expense);
        var rates = await _store.AddItemAsync(home.Id, null, "Rates", ItemKind.Expense);
        var water = await _store.AddItemAsync(home.Id, rates.Id, "Water", ItemKind.Expense);
        var food = await _store.AddItemAsync(daily.Id, null, "Food", ItemKind.Expense);
        var gifts = await _store.AddItemAsync(daily.Id, null, "Gifts", ItemKind.Expense);

        Assert.Null(await _store.MoveItemToAsync(rates.Id, daily.Id, null, gifts.Id));

        var inDaily = (await _store.GetItemsAsync()).Where(i => i.GroupId == daily.Id && i.ParentItemId == null).Select(i => i.Name).ToArray();
        Assert.Equal(new[] { "Food", "Rates", "Gifts" }, inDaily);
        Assert.Equal(daily.Id, water.GroupId);
        Assert.Equal(rates.Id, water.ParentItemId);
    }

    [Fact]
    public async Task A_moved_item_goes_last_when_no_place_is_given_and_can_become_a_sub_item()
    {
        var group = await _store.AddGroupAsync("Daily", ItemKind.Expense);
        var a = await _store.AddItemAsync(group.Id, null, "A", ItemKind.Expense);
        var b = await _store.AddItemAsync(group.Id, null, "B", ItemKind.Expense);

        Assert.Null(await _store.MoveItemToAsync(a.Id, group.Id, null, null));
        Assert.Equal(new[] { "B", "A" }, (await _store.GetItemsAsync()).Select(i => i.Name).ToArray());

        Assert.Null(await _store.MoveItemToAsync(a.Id, group.Id, b.Id, null));
        Assert.Equal(b.Id, a.ParentItemId);
    }

    [Fact]
    public async Task Items_cannot_move_into_the_other_kind_of_group_or_inside_themselves()
    {
        var expense = await _store.AddGroupAsync("Daily", ItemKind.Expense);
        var income = await _store.AddGroupAsync("Income", ItemKind.Income);
        var parent = await _store.AddItemAsync(expense.Id, null, "Parent", ItemKind.Expense);
        var child = await _store.AddItemAsync(expense.Id, parent.Id, "Child", ItemKind.Expense);

        Assert.NotNull(await _store.MoveItemToAsync(parent.Id, income.Id, null, null));
        Assert.NotNull(await _store.MoveItemToAsync(parent.Id, expense.Id, child.Id, null));
        Assert.NotNull(await _store.MoveItemToAsync(parent.Id, expense.Id, parent.Id, null));
        Assert.Equal(expense.Id, parent.GroupId);
    }

    [Fact]
    public async Task An_items_emoji_shows_in_front_of_its_name_in_the_budget_and_can_be_removed()
    {
        var group = await _store.AddGroupAsync("Daily", ItemKind.Expense);
        var item = await _store.AddItemAsync(group.Id, null, "Groceries", ItemKind.Expense);
        Assert.Equal("Groceries", item.DisplayName);

        item.Emoji = "🛒";
        await _store.SaveAsync();
        Assert.Equal("🛒 Groceries", (await _store.LoadSnapshotAsync()).Items.Single().Name);

        item.Emoji = null;
        await _store.SaveAsync();
        Assert.Equal("Groceries", (await _store.LoadSnapshotAsync()).Items.Single().Name);
    }

    [Fact]
    public async Task Local_settings_are_remembered_and_updated()
    {
        Assert.Null(await _store.GetLocalSettingAsync("budgetView"));
        await _store.SetLocalSettingAsync("budgetView", "single");
        await _store.SetLocalSettingAsync("budgetView", "many");

        Assert.Equal("many", await _store.GetLocalSettingAsync("budgetView"));
    }

    [Fact]
    public async Task Items_can_be_pinned_to_and_removed_from_the_watcher_strip()
    {
        var group = await _store.AddGroupAsync("Daily", ItemKind.Expense);
        var food = await _store.AddItemAsync(group.Id, null, "Food", ItemKind.Expense);
        var petrol = await _store.AddItemAsync(group.Id, null, "Petrol", ItemKind.Expense);
        var salary = await _store.AddItemAsync((await _store.AddGroupAsync("Income", ItemKind.Income)).Id, null, "Salary", ItemKind.Income);

        await _store.SetPinnedAsync(food.Id, true);
        await _store.SetPinnedAsync(petrol.Id, true);
        await _store.SetPinnedAsync(salary.Id, true);
        Assert.Equal(new[] { food.Id, petrol.Id }, await _store.GetPinnedItemIdsAsync());   // income items never show on the strip

        await _store.SetPinnedAsync(food.Id, false);
        await _store.SetPinnedAsync(food.Id, false);   // removing twice is harmless
        Assert.Equal(new[] { petrol.Id }, await _store.GetPinnedItemIdsAsync());
    }

    [Fact]
    public async Task The_watcher_strip_holds_at_most_three_items()
    {
        var group = await _store.AddGroupAsync("Daily", ItemKind.Expense);
        for (var i = 1; i <= 5; i++)
            await _store.SetPinnedAsync((await _store.AddItemAsync(group.Id, null, $"Item {i}", ItemKind.Expense)).Id, true);
        Assert.Equal(BudgetStore.MaxPinnedItems, (await _store.GetPinnedItemIdsAsync()).Count);
    }

    [Fact]
    public async Task Setup_creates_the_chosen_accounts_groups_items_and_counterparties()
    {
        var plan = SetupTemplates.Create("Minimal");
        plan.Accounts.Add(new SetupAccount { Name = "Retirement", Type = AccountType.Investment, IsOnBudget = false, OpeningBalance = 5_000_00 });
        plan.Groups[1].Items[1].Selected = false;                      // Utilities unticked
        plan.Counterparties.Add(new SetupCounterparty("Woolworths"));
        plan.Counterparties.Add(new SetupCounterparty("Checkers", selected: false));

        var result = await _store.ApplySetupAsync(plan);

        Assert.Equal(new SetupResult(2, 4, 6, 1), result);
        Assert.Equal(new[] { "Cheque account", "Retirement" }, (await _store.GetAccountsAsync()).Select(a => a.Name));
        Assert.False((await _store.GetAccountsAsync()).Single(a => a.Name == "Retirement").IsOnBudget);
        var items = await _store.GetItemsAsync();
        Assert.DoesNotContain(items, i => i.Name == "Utilities");
        Assert.Equal("🛒", items.Single(i => i.Name == "Groceries").Emoji);
        Assert.Equal(ItemKind.Income, items.Single(i => i.Name == "Salary").Kind);
        Assert.Equal(new[] { "Woolworths" }, (await _store.GetPayeesAsync()).Select(p => p.Name));
    }

    [Fact]
    public async Task Running_setup_again_adds_only_what_is_missing()
    {
        await _store.ApplySetupAsync(SetupTemplates.Create("Household"));
        var again = SetupTemplates.Create("Household");
        again.Groups[0].Items.Add(new SetupItem("Bonus"));
        again.Groups[0].Name = "income";   // same group, different case

        var result = await _store.ApplySetupAsync(again);

        Assert.Equal(new SetupResult(0, 0, 1, 0), result);
        Assert.Single(await _store.GetAccountsAsync());
        Assert.Equal(8, (await _store.GetGroupsAsync()).Count);
    }

    [Fact]
    public async Task Reset_removes_everything_so_the_app_is_empty_again()
    {
        await _store.ApplySetupAsync(SetupTemplates.Create("Household"));
        var account = (await _store.GetAccountsAsync()).Single();
        await AddTx(account, new(2027, 1, 3), -10_00, payee: "Shoprite");
        await _store.SetSettingAsync("x", "y");
        await _store.SetLocalSettingAsync("budgetView", "single");

        await _store.ResetAllAsync();

        Assert.True(await _store.IsEmptyAsync());
        Assert.Empty(await _store.GetItemsAsync());
        Assert.Empty(await _store.GetPayeesAsync());
        Assert.Empty(await _store.GetTransactionsAsync());
        Assert.Empty(await _store.GetMarkersAsync());
        Assert.Null(await _store.GetSettingAsync("x"));
        Assert.Null(await _store.GetLocalSettingAsync("budgetView"));

        await _store.ApplySetupAsync(SetupTemplates.Create("Minimal"));   // and it can be set up again
        Assert.False(await _store.IsEmptyAsync());
    }

    [Fact]
    public async Task Setup_skips_a_new_group_with_nothing_ticked()
    {
        var plan = new SetupPlan();
        plan.Groups.Add(new SetupGroup("Pets", ItemKind.Expense, [new SetupItem("Food", selected: false)]));

        Assert.Equal(new SetupResult(0, 0, 0, 0), await _store.ApplySetupAsync(plan));
        Assert.Empty(await _store.GetGroupsAsync());
    }

    [Fact]
    public async Task Payees_are_created_once_ignoring_case_and_picked_up_from_new_transactions()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true);
        await _store.EnsurePayeeAsync("Polo Petrol");
        await _store.EnsurePayeeAsync("  polo petrol ");
        await AddTx(a, new(2027, 1, 3), -10_00, payee: "Shoprite");
        await AddTx(a, new(2027, 1, 4), -10_00, payee: "SHOPRITE");

        var names = (await _store.GetPayeesAsync()).Select(p => p.Name).ToArray();

        Assert.Equal(new[] { "Polo Petrol", "Shoprite" }, names);
    }

    [Fact]
    public async Task Existing_transaction_payees_can_be_imported_into_the_list_and_transfers_are_skipped()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true);
        var b = await _store.AddAccountAsync("B", AccountType.Savings, false);
        _db.Transactions.Add(new TransactionEntity { AccountId = a.Id, Date = new(2027, 1, 3), Amount = -5_00, Payee = "Clicks" });
        await _db.SaveChangesAsync();
        var saving = AddItem("Savings", ItemKind.Expense);
        await _store.AddTransferAsync(a.Id, b.Id, new(2027, 1, 4), 10_00, null, saving.Id);

        await _store.EnsurePayeesFromTransactionsAsync();
        await _store.EnsurePayeesFromTransactionsAsync();

        Assert.Equal("Clicks", Assert.Single(await _store.GetPayeesAsync()).Name);
    }

    [Fact]
    public async Task Renaming_a_payee_renames_it_on_its_transactions_and_refuses_a_name_already_taken()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true);
        await AddTx(a, new(2027, 1, 3), -10_00, payee: "Shop rite");
        await AddTx(a, new(2027, 1, 4), -10_00, payee: "Clicks");
        var payees = await _store.GetPayeesAsync();
        var shop = payees.Single(p => p.Name == "Shop rite");

        Assert.Equal("There is already a counterparty with that name.", await _store.RenamePayeeAsync(shop, "clicks"));
        Assert.Null(await _store.RenamePayeeAsync(shop, "Shoprite"));

        var transactions = (await _store.GetTransactionsAsync()).Select(t => t.Transaction.Payee).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "Clicks", "Shoprite" }, transactions);
        Assert.Equal("Shoprite", shop.Name);
    }

    [Fact]
    public async Task A_payee_in_use_cannot_be_deleted_but_an_unused_one_can()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true);
        await AddTx(a, new(2027, 1, 3), -10_00, payee: "Used");
        var unused = await _store.EnsurePayeeAsync("Unused");
        var used = (await _store.GetPayeesAsync()).Single(p => p.Name == "Used");

        Assert.False(await _store.DeletePayeeAsync(used));
        Assert.True(await _store.DeletePayeeAsync(unused));
        Assert.Equal(1, await _store.CountPayeeTransactionsAsync("used"));
        Assert.Equal("Used", Assert.Single(await _store.GetPayeesAsync()).Name);
    }

    [Fact]
    public async Task First_month_is_the_current_month_until_the_first_budget_transaction_and_its_month_is_one_row()
    {
        Assert.Equal(MonthLabels.For(DateOnly.FromDateTime(DateTime.Today)), await _store.GetFirstMonthLabelAsync());

        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        await AddTx(cheque, new(2024, 5, 9), -10_00);
        await _store.SetFirstMonthLabelAsync("March 2024");
        await _store.SetFirstMonthLabelAsync("April 2024");

        Assert.Equal("April 2024", await _store.GetFirstMonthLabelAsync());
        Assert.Single(_db.Settings.ToList());
        Assert.Equal(SettingEntity.IdFor("firstMonthLabel"), _db.Settings.Single().Id);
        Assert.True(await _store.HasBudgetTransactionsAsync());
    }

    [Fact]
    public async Task Only_transactions_in_budget_accounts_count_as_the_first_budget_transaction()
    {
        var tracked = await _store.AddAccountAsync("Retirement", AccountType.Investment, false);
        await AddTx(tracked, new(2024, 5, 9), -10_00);
        Assert.False(await _store.HasBudgetTransactionsAsync());

        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        await AddTx(cheque, new(2024, 5, 10), -10_00);
        Assert.True(await _store.HasBudgetTransactionsAsync());
    }

    [Fact]
    public async Task Going_back_stops_at_the_earliest_month()
    {
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        await AddTx(cheque, new(1950, 1, 5), -10_00);
        await _store.SetFirstMonthLabelAsync("January 1950");

        Assert.Null(await _store.AddEarlierMonthAsync());
    }

    [Fact]
    public async Task Starting_a_month_moves_the_budget_already_set_for_that_month_to_its_line()
    {
        var food = AddItem("Food", ItemKind.Expense);
        var february = BudgetEngine.VirtualMonthId(new DateOnly(2027, 2, 1));
        await _store.SetAllocationAsync(february, food.Id, 100_00);
        await _store.SetAllocationAsync(BudgetEngine.VirtualMonthId(new DateOnly(2027, 4, 1)), food.Id, 300_00);

        var line = await _store.AddMarkerAsync("February 2027", new(2027, 2, 1), copyPreviousBudget: false);

        long Of(Guid month) => _db.Allocations.Where(a => a.MonthId == month).Select(a => a.Amount).ToList().DefaultIfEmpty(0).Sum();
        Assert.Equal(100_00, Of(line.Id));
        Assert.Equal(0, Of(february));
        Assert.Equal(300_00, Of(BudgetEngine.VirtualMonthId(new DateOnly(2027, 4, 1))));   // other months are left alone
    }

    [Fact]
    public async Task Budgets_kept_under_the_old_planned_month_ids_move_to_their_calendar_months_once()
    {
        var food = AddItem("Food", ItemKind.Expense);
        await _store.AddMarkerAsync("February 2027", new(2027, 2, 1), copyPreviousBudget: false);
        await _store.SetAllocationAsync(BudgetEngine.LegacyPlannedMonthId(1), food.Id, 100_00);
        await _store.SetAllocationAsync(BudgetEngine.LegacyPlannedMonthId(2), food.Id, 200_00);

        await _store.MigrateLegacyPlannedMonthsAsync();
        await _store.MigrateLegacyPlannedMonthsAsync();   // running it again changes nothing

        long Of(Guid month) => _db.Allocations.Where(a => a.MonthId == month).Select(a => a.Amount).ToList().DefaultIfEmpty(0).Sum();
        Assert.Equal(100_00, Of(BudgetEngine.VirtualMonthId(new DateOnly(2027, 3, 1))));
        Assert.Equal(200_00, Of(BudgetEngine.VirtualMonthId(new DateOnly(2027, 4, 1))));
        Assert.Equal(0, Of(BudgetEngine.LegacyPlannedMonthId(1)));
    }

    [Fact]
    public async Task A_forgotten_month_between_two_lines_is_shown_and_can_be_budgeted()
    {
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        var food = AddItem("Food", ItemKind.Expense);
        await AddTx(cheque, new(2026, 5, 20), -10_00, food.Id);
        await _store.SetFirstMonthLabelAsync("May 2026");
        await _store.AddMarkerAsync("June 2026", new(2026, 6, 1), copyPreviousBudget: false);
        await _store.AddMarkerAsync("September 2026", new(2026, 9, 1), copyPreviousBudget: false);

        var months = BudgetEngine.Calculate(await _store.LoadSnapshotAsync());

        Assert.Equal(new[] { "May 2026", "June 2026", "July 2026", "August 2026", "September 2026" }, months.Select(m => m.Label));
        await _store.SetAllocationAsync(months[2].MonthId, food.Id, 250_00);
        Assert.Equal(250_00, BudgetEngine.Calculate(await _store.LoadSnapshotAsync())[2].Budgeted);
    }

    [Fact]
    public async Task A_transaction_on_a_month_lines_date_can_be_placed_in_either_month_by_its_position_in_the_day()
    {
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        var line = await _store.AddMarkerAsync("February 2027", new(2027, 2, 1), copyPreviousBudget: false);
        var lines = BudgetEngine.SortMarkers((await _store.GetMarkersAsync()).Select(m => new MonthMarker(m.Id, m.Date, m.DayOrder, m.Label)));

        // Just before the line's own position: in the month that ends that day. At it, or after: in the month that starts that day.
        Assert.Equal(BudgetEngine.FirstMonthId, BudgetEngine.MonthOf(lines, line.Date, line.DayOrder - 1));
        Assert.Equal(line.Id, BudgetEngine.MonthOf(lines, line.Date, line.DayOrder));

        var transfer = await _store.AddTransferAsync(cheque.Id, (await _store.AddAccountAsync("Savings", AccountType.Savings, true)).Id,
            line.Date, 100_00, null, null, dayOrder: line.DayOrder - 1);
        Assert.Equal(line.DayOrder - 1, transfer.From.DayOrder);
        Assert.Equal(line.DayOrder - 1, transfer.To.DayOrder);
    }

    [Fact]
    public async Task Moving_a_month_line_to_a_new_date_is_allowed_between_its_neighbours_only()
    {
        var feb = await _store.AddMarkerAsync("February 2027", new(2027, 2, 1), copyPreviousBudget: false);
        var mar = await _store.AddMarkerAsync("March 2027", new(2027, 3, 1), copyPreviousBudget: false);

        Assert.True(await _store.MoveMarkerAsync(feb, new SortKey(new(2027, 2, 15), await _store.NextDayOrderAsync(new(2027, 2, 15)))));
        Assert.Equal(new DateOnly(2027, 2, 15), (await _store.GetMarkersAsync()).Single(m => m.Id == feb.Id).Date);
        Assert.False(await _store.MoveMarkerAsync(feb, new SortKey(new(2027, 3, 10), 0)));   // past March's line: refused
    }

    [Fact]
    public void The_data_folder_from_older_versions_is_moved_to_its_new_name_with_everything_in_it()
    {
        var local = Directory.CreateTempSubdirectory("sb-local").FullName;
        try
        {
            var old = Path.Combine(local, "SpeedBudget");
            Directory.CreateDirectory(Path.Combine(old, "backups"));
            File.WriteAllText(Path.Combine(old, "budget.db"), "my budget");
            File.WriteAllText(Path.Combine(old, "backups", "b1.db"), "backup");

            var folder = LocalDatabase.ResolveDirectory(local);

            Assert.Equal(Path.Combine(local, "SpeedBudget-Data"), folder);
            Assert.Equal("my budget", File.ReadAllText(Path.Combine(folder, "budget.db")));
            Assert.Equal("backup", File.ReadAllText(Path.Combine(folder, "backups", "b1.db")));
            Assert.False(Directory.Exists(old));
            Assert.True(File.Exists(Path.Combine(folder, "README.txt")));
        }
        finally { Directory.Delete(local, recursive: true); }
    }

    [Fact]
    public void An_existing_new_data_folder_is_used_as_it_is_and_the_old_one_is_left_alone()
    {
        var local = Directory.CreateTempSubdirectory("sb-local").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(local, "SpeedBudget"));
            File.WriteAllText(Path.Combine(local, "SpeedBudget", "budget.db"), "old");
            Directory.CreateDirectory(Path.Combine(local, "SpeedBudget-Data"));
            File.WriteAllText(Path.Combine(local, "SpeedBudget-Data", "budget.db"), "new");

            var folder = LocalDatabase.ResolveDirectory(local);

            Assert.Equal("new", File.ReadAllText(Path.Combine(folder, "budget.db")));
            Assert.Equal("old", File.ReadAllText(Path.Combine(local, "SpeedBudget", "budget.db")));   // never overwritten or deleted
        }
        finally { Directory.Delete(local, recursive: true); }
    }

    [Fact]
    public void A_fresh_install_gets_an_empty_data_folder_with_a_readme()
    {
        var local = Directory.CreateTempSubdirectory("sb-local").FullName;
        try
        {
            var folder = LocalDatabase.ResolveDirectory(local);

            Assert.Equal(Path.Combine(local, "SpeedBudget-Data"), folder);
            Assert.Contains("do not delete", File.ReadAllText(Path.Combine(folder, "README.txt")), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(new[] { "README.txt" }, Directory.GetFileSystemEntries(folder).Select(Path.GetFileName));
        }
        finally { Directory.Delete(local, recursive: true); }
    }

    [Fact]
    public async Task Balances_cover_one_account_or_all_of_them()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true, openingBalance: 1_000_00);
        var b = await _store.AddAccountAsync("B", AccountType.Savings, false, openingBalance: 500_00);
        var cleared = await AddTx(a, new(2027, 1, 2), -100_00);
        cleared.IsCleared = true;
        await _store.SaveAsync();
        await AddTx(a, new(2027, 1, 3), -40_00);
        await AddTx(b, new(2027, 1, 4), 60_00);

        var onlyA = await _store.GetBalancesAsync(a.Id);
        var all = await _store.GetBalancesAsync();

        Assert.Equal(900_00, onlyA.ClearedBalance);
        Assert.Equal(-40_00, onlyA.UnclearedTotal);
        Assert.Equal(860_00, onlyA.BookBalance);
        Assert.Equal(1_420_00, all.BookBalance);
        Assert.Equal(1_400_00, all.ClearedBalance);
    }

    [Fact]
    public async Task Reconciling_marks_cleared_transactions_only_when_the_bank_balance_matches()
    {
        var a = await _store.AddAccountAsync("A", AccountType.Cheque, true, openingBalance: 1_000_00);
        var cleared = await AddTx(a, new(2027, 1, 2), -100_00);
        cleared.IsCleared = true;
        var pending = await AddTx(a, new(2027, 1, 3), -40_00);
        await _store.SaveAsync();

        var wrong = await _store.ReconcileAsync(a.Id, 950_00);
        Assert.False(wrong.Reconciled);
        Assert.Equal(50_00, wrong.Result.Difference);
        Assert.False(cleared.IsReconciled);

        var right = await _store.ReconcileAsync(a.Id, 900_00);
        Assert.True(right.Reconciled);
        Assert.True(cleared.IsReconciled);
        Assert.False(pending.IsReconciled);
        Assert.Equal(900_00, await _store.GetStatementBalanceAsync(a.Id));
    }

    [Fact]
    public async Task Payment_method_and_heading_are_stored_on_items()
    {
        var group = await _store.AddGroupAsync("Utilities", ItemKind.Expense);
        var item = await _store.AddItemAsync(group.Id, null, "Rates", ItemKind.Expense);

        item.PaymentMethod = PaymentMethod.Eft;
        item.ReportHeading = "Utilities";
        await _store.SaveAsync();

        var reloaded = (await _store.GetItemsAsync()).Single();
        Assert.Equal(PaymentMethod.Eft, reloaded.PaymentMethod);
        Assert.Equal("Utilities", reloaded.ReportHeading);
    }

    [Fact]
    public async Task Going_back_a_month_keeps_the_old_first_month_intact_and_adds_an_empty_one_before_it()
    {
        await _store.SetFirstMonthLabelAsync("March 2024");
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        var food = AddItem("Food", ItemKind.Expense);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, food.Id, 800_00);
        var tx = await AddTx(cheque, new(2024, 3, 12), -50_00, food.Id);

        var marker = await _store.AddEarlierMonthAsync();

        Assert.NotNull(marker);
        var snapshot = await _store.LoadSnapshotAsync();
        var months = BudgetEngine.Calculate(snapshot);
        Assert.Equal(new[] { "February 2024", "March 2024" }, months.Take(2).Select(m => m.Label));
        // The 800 budgeted and the 50 spent stay with March; the new February is empty.
        Assert.Equal(0, months[0].Items.Sum(i => i.Budgeted + i.Activity));
        Assert.Equal(800_00, months[1].Items.Single(i => i.ItemId == food.Id).Budgeted);
        Assert.Equal(-50_00, months[1].Items.Single(i => i.ItemId == food.Id).Activity);
        Assert.Equal(marker!.Id, BudgetEngine.MonthOf(BudgetEngine.SortMarkers(snapshot.Markers), tx.Date, tx.DayOrder));

        // And it can keep going back, a month at a time.
        await _store.AddEarlierMonthAsync();
        Assert.Equal("January 2024", (await _store.GetFirstMonthLabelAsync()));
    }

    [Fact]
    public async Task Renaming_the_first_month_keeps_the_name_even_when_data_is_earlier()
    {
        var cheque = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        await AddTx(cheque, new(2024, 1, 5), -10_00);

        await _store.RenameFirstMonthAsync("Opening balances");

        Assert.Equal("Opening balances", await _store.GetFirstMonthLabelAsync());
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

    [Fact]
    public async Task Budget_can_be_moved_between_items_in_one_save()
    {
        var group = await _store.AddGroupAsync("Everyday", ItemKind.Expense);
        var food = await _store.AddItemAsync(group.Id, null, "Food", ItemKind.Expense);
        var fun = await _store.AddItemAsync(group.Id, null, "Fun", ItemKind.Expense);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, food.Id, 1_000_00);

        await _store.AdjustAllocationsAsync(BudgetEngine.FirstMonthId, [(food.Id, -300_00), (fun.Id, 300_00)]);

        var items = BudgetEngine.Calculate(await _store.LoadSnapshotAsync())[0].Items;
        Assert.Equal(700_00, items.Single(x => x.ItemId == food.Id).Budgeted);
        Assert.Equal(300_00, items.Single(x => x.ItemId == fun.Id).Budgeted);
    }

    [Fact]
    public void The_budget_looks_twelve_months_ahead()
    {
        var snapshot = new BudgetSnapshot([], [], [], [], [], []);
        Assert.Equal(12, BudgetEngine.PlannedMonthCount);
        var months = BudgetEngine.Calculate(snapshot, BudgetEngine.PlannedMonthCount);
        Assert.True(months.Count(m => m.IsPlanned) >= 12);
    }

    [Fact]
    public async Task An_expected_transaction_becomes_a_real_one_on_the_date_it_happens()
    {
        var cheque = AddAccount("Cheque", true);
        var group = await _store.AddGroupAsync("Everyday", ItemKind.Expense);
        var food = await _store.AddItemAsync(group.Id, null, "Food", ItemKind.Expense);
        var expected = await _store.AddExpectedAsync(new ExpectedTransactionEntity
            { AccountId = cheque.Id, Payee = " Woolworths ", Amount = -250_00, BudgetItemId = food.Id, ExpectedDate = new(2027, 3, 1) });
        Assert.Single(await _store.GetExpectedAsync(cheque.Id));
        Assert.Empty(await _store.GetTransactionsAsync(cheque.Id));

        var recorded = await _store.RecordExpectedAsync(expected, new(2027, 3, 4));

        Assert.Empty(await _store.GetExpectedAsync(cheque.Id));
        var saved = Assert.Single(await _store.GetTransactionsAsync(cheque.Id));
        Assert.Equal(recorded.Id, saved.Transaction.Id);
        Assert.Equal(new DateOnly(2027, 3, 4), saved.Transaction.Date);
        Assert.Equal("Woolworths", saved.Transaction.Payee);
        Assert.Equal(food.Id, Assert.Single(saved.Lines).BudgetItemId);
    }

    [Fact]
    public async Task A_budget_item_and_its_group_can_be_edited_together()
    {
        var everyday = await _store.AddGroupAsync("Everyday", ItemKind.Expense);
        var fun = await _store.AddGroupAsync("Fun", ItemKind.Expense);
        var food = await _store.AddItemAsync(everyday.Id, null, "Food", ItemKind.Expense);

        var error = await _store.UpdateItemAsync(food.Id, "Groceries", "🛒", fun.Id, "Leisure");

        Assert.Null(error);
        var item = (await _store.GetItemsAsync()).Single(i => i.Id == food.Id);
        Assert.Equal("Groceries", item.Name);
        Assert.Equal("🛒", item.Emoji);
        Assert.Equal(fun.Id, item.GroupId);
        Assert.Equal("Leisure", (await _store.GetGroupsAsync()).Single(g => g.Id == fun.Id).Name);
    }

    [Fact]
    public async Task A_counterparty_keeps_the_icon_it_is_given()
    {
        var payee = await _store.EnsurePayeeAsync("Woolworths");
        await _store.SetPayeeEmojiAsync(payee, "🛒");
        Assert.Equal("🛒", (await _store.GetPayeesAsync()).Single().Emoji);

        await _store.SetPayeeEmojiAsync(payee, "");
        Assert.Null((await _store.GetPayeesAsync()).Single().Emoji);
    }

    [Fact]
    public async Task Undo_puts_back_a_budgeted_amount_and_redo_reapplies_it()
    {
        var food = AddItem("Food", ItemKind.Expense);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, food.Id, 500_00);
        _store.StartRecordingUndo();
        await Task.Delay(800);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, food.Id, 700_00);
        Assert.Equal(700_00, _db.Allocations.Single().Amount);

        var undone = await _store.UndoAsync();
        Assert.NotNull(undone);
        Assert.Equal(500_00, _db.Allocations.Single().Amount);

        await _store.UndoAsync(redo: true);
        Assert.Equal(700_00, _db.Allocations.Single().Amount);
        Assert.Null(await _store.UndoAsync(redo: true));
    }

    [Fact]
    public async Task Undo_brings_back_a_deleted_transaction_with_its_lines_and_removes_an_added_one()
    {
        var account = AddAccount("Cheque", true);
        var food = AddItem("Food", ItemKind.Expense);
        _store.StartRecordingUndo();

        await _store.AddTransactionAsync(
            new TransactionEntity { AccountId = account.Id, Date = new DateOnly(2026, 11, 3), Payee = "Shop", Amount = -50_00 },
            new[] { new SplitLineEntity { BudgetItemId = food.Id, Amount = -50_00 } });
        Assert.Single(await _store.GetTransactionsAsync());
        await _store.UndoAsync();
        Assert.Empty(await _store.GetTransactionsAsync());   // the added transaction is gone again

        await _store.UndoAsync(redo: true);
        Assert.Single(await _store.GetTransactionsAsync());

        await Task.Delay(800);
        await _store.DeleteTransactionAsync((await _store.GetTransactionsAsync()).Single().Transaction);
        Assert.Empty(await _store.GetTransactionsAsync());
        await _store.UndoAsync();
        var back = Assert.Single(await _store.GetTransactionsAsync());
        Assert.Equal(-50_00, back.Transaction.Amount);
        Assert.Single(back.Lines);
    }

    [Fact]
    public async Task Actions_before_recording_starts_cannot_be_undone()
    {
        var food = AddItem("Food", ItemKind.Expense);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, food.Id, 500_00);
        Assert.False(_store.CanUndo);
        Assert.Null(await _store.UndoAsync());
    }

    [Fact]
    public async Task A_group_can_be_renamed_and_an_item_renamed_and_given_an_icon_on_its_own()
    {
        var group = await _store.AddGroupAsync("Everyday", ItemKind.Expense);
        var item = await _store.AddItemAsync(group.Id, null, "Food", ItemKind.Expense);

        await _store.RenameGroupAsync(group.Id, "Daily life");
        await _store.EditItemAsync(item.Id, "Groceries", null);
        await _store.EditItemAsync(item.Id, null, "🛒");

        Assert.Equal("Daily life", (await _store.GetGroupsAsync()).Single().Name);
        var saved = (await _store.GetItemsAsync()).Single();
        Assert.Equal("Groceries", saved.Name);
        Assert.Equal("🛒", saved.Emoji);

        await _store.EditItemAsync(item.Id, "  ", "");   // a blank name changes nothing; an empty icon removes it
        saved = (await _store.GetItemsAsync()).Single();
        Assert.Equal("Groceries", saved.Name);
        Assert.Null(saved.Emoji);
    }

    [Fact]
    public async Task An_account_can_be_edited_in_every_detail()
    {
        var account = await _store.AddAccountAsync("Old", AccountType.Cheque, isOnBudget: true, openingBalance: 100_00);

        await _store.UpdateAccountAsync(account.Id, "  New name ", AccountType.Savings, isOnBudget: false, openingBalance: 250_00, isClosed: true);

        var saved = (await _store.GetAccountsAsync()).Single();
        Assert.Equal("New name", saved.Name);
        Assert.Equal(AccountType.Savings, saved.Type);
        Assert.False(saved.IsOnBudget);
        Assert.Equal(250_00, saved.OpeningBalance);
        Assert.True(saved.IsClosed);
    }

    [Fact]
    public async Task A_group_can_be_moved_before_or_after_another_group()
    {
        var a = await _store.AddGroupAsync("A", ItemKind.Expense);
        var b = await _store.AddGroupAsync("B", ItemKind.Expense);
        var c = await _store.AddGroupAsync("C", ItemKind.Expense);

        await _store.MoveGroupToAsync(c.Id, a.Id, after: false);
        Assert.Equal(new[] { "C", "A", "B" }, (await _store.GetGroupsAsync()).Select(g => g.Name));

        await _store.MoveGroupToAsync(c.Id, b.Id, after: true);
        Assert.Equal(new[] { "A", "B", "C" }, (await _store.GetGroupsAsync()).Select(g => g.Name));
    }

    [Fact]
    public async Task Deleting_a_group_removes_it_and_its_items_only_when_none_has_been_used()
    {
        var empty = await _store.AddGroupAsync("Empty", ItemKind.Expense);
        await _store.AddItemAsync(empty.Id, null, "Unused", ItemKind.Expense);
        var result = await _store.DeleteGroupWithItemsAsync(empty.Id);
        Assert.Equal((true, (string?)null), result);
        Assert.Empty(await _store.GetGroupsAsync());

        var used = await _store.AddGroupAsync("Used", ItemKind.Expense);
        await _store.AddItemAsync(used.Id, null, "Spare", ItemKind.Expense);
        var item = await _store.AddItemAsync(used.Id, null, "Food", ItemKind.Expense);
        await _store.SetAllocationAsync(BudgetEngine.FirstMonthId, item.Id, 100_00);
        result = await _store.DeleteGroupWithItemsAsync(used.Id);
        Assert.False(result.Deleted);
        Assert.Equal("Food can't be deleted because it has budget on it.", result.Blocker);
        Assert.Single(await _store.GetGroupsAsync());      // nothing was deleted
        Assert.Equal(2, (await _store.GetItemsAsync()).Count);   // not even the item that was never used
    }

    [Fact]
    public async Task Dated_expected_transactions_are_recorded_by_themselves_and_repeating_ones_move_on()
    {
        var account = AddAccount("Cheque", true);
        await _store.AddExpectedAsync(new ExpectedTransactionEntity { AccountId = account.Id, Payee = "Someday", Amount = -100 });
        await _store.AddExpectedAsync(new ExpectedTransactionEntity { AccountId = account.Id, Payee = "Once", Amount = -200, Kind = ExpectedKind.Once, ExpectedDate = new DateOnly(2027, 3, 10) });
        await _store.AddExpectedAsync(new ExpectedTransactionEntity { AccountId = account.Id, Payee = "Later", Amount = -300, Kind = ExpectedKind.Once, ExpectedDate = new DateOnly(2027, 3, 16) });
        await _store.AddExpectedAsync(new ExpectedTransactionEntity
        {
            AccountId = account.Id, Payee = "Rent", Amount = -400, Kind = ExpectedKind.Repeating, Repeat = ExpectedRepeat.Monthly,
            ExpectedDate = new DateOnly(2027, 1, 31), AnchorDay = 31,
        });

        var added = await _store.RecordDueExpectedAsync(new DateOnly(2027, 3, 15));

        Assert.Equal(3, added);   // the once-off one, and the rent of 31 Jan and 28 Feb
        var recorded = (await _store.GetTransactionsAsync(account.Id)).Select(t => (t.Transaction.Payee, t.Transaction.Date)).Order().ToList();
        Assert.Equal(
            [("Once", new DateOnly(2027, 3, 10)), ("Rent", new DateOnly(2027, 1, 31)), ("Rent", new DateOnly(2027, 2, 28))],
            recorded);

        var left = await _store.GetExpectedAsync(account.Id);
        Assert.Equal(["Later", "Rent", "Someday"], left.Select(e => e.Payee).Order());
        Assert.Equal(new DateOnly(2027, 3, 31), left.Single(e => e.Payee == "Rent").ExpectedDate);   // back on the 31st
        Assert.Equal(0, await _store.RecordDueExpectedAsync(new DateOnly(2027, 3, 15)));   // nothing is recorded twice
    }

    [Fact]
    public async Task Watcher_strip_keeps_the_order_items_were_dragged_into()
    {
        var a = AddItem("Rent", ItemKind.Expense);
        var b = AddItem("Fuel", ItemKind.Expense);
        var c = AddItem("Food", ItemKind.Expense);
        await _store.SetPinnedAsync(b.Id, true);
        await _store.SetPinnedAsync(a.Id, true);
        await _store.SetPinnedAsync(c.Id, true);
        Assert.Equal([b.Id, a.Id, c.Id], await _store.GetPinnedItemIdsAsync());   // newest pins go last

        await _store.SetPinOrderAsync([c.Id, a.Id, b.Id]);
        Assert.Equal([c.Id, a.Id, b.Id], await _store.GetPinnedItemIdsAsync());
    }
}
