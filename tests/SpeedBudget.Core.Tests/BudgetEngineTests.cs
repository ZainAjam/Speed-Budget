using SpeedBudget.Core;

namespace SpeedBudget.Core.Tests;

public class BudgetEngineTests
{
    private static readonly Guid Cheque = Guid.NewGuid();
    private static readonly Guid Savings = Guid.NewGuid();
    private static readonly Guid Salary = Guid.NewGuid();
    private static readonly Guid Petrol = Guid.NewGuid();
    private static readonly Guid Food = Guid.NewGuid();

    private static readonly Account[] Accounts =
    {
        new(Cheque, "Cheque", IsOnBudget: true),
        new(Savings, "Savings pocket", IsOnBudget: false),
    };

    private static readonly BudgetItem[] Items =
    {
        new(Salary, "Salary", ItemKind.Income),
        new(Petrol, "Petrol", ItemKind.Expense),
        new(Food, "Food", ItemKind.Expense),
    };

    private static DateOnly D(int month, int day) => new(2027, month, day);

    private static Transaction Tx(Guid account, DateOnly date, int order, long amount, Guid? item) =>
        new(Guid.NewGuid(), account, date, order, amount, new[] { new SplitLine(item, amount) });

    private static MonthSummary Month(IReadOnlyList<MonthSummary> all, Guid id) => all.Single(m => m.MonthId == id);

    private static BudgetSnapshot Snapshot(
        IEnumerable<Transaction> txs,
        IEnumerable<MonthMarker>? markers = null,
        IEnumerable<Allocation>? allocations = null,
        IEnumerable<IncomeHold>? holds = null) =>
        new(Accounts, Items, (markers ?? Array.Empty<MonthMarker>()).ToList(), txs.ToList(),
            (allocations ?? Array.Empty<Allocation>()).ToList(), (holds ?? Array.Empty<IncomeHold>()).ToList());

    [Fact]
    public void Single_month_available_is_income_minus_budgeted()
    {
        var snap = Snapshot(
            new[] { Tx(Cheque, D(1, 1), 0, 10_000_00, Salary) },
            allocations: new[]
            {
                new Allocation(BudgetEngine.FirstMonthId, Petrol, 1_000_00),
                new Allocation(BudgetEngine.FirstMonthId, Food, 3_000_00),
            });

        var m = BudgetEngine.Calculate(snap).Single();

        Assert.Equal(10_000_00, m.Income);
        Assert.Equal(4_000_00, m.Budgeted);
        Assert.Equal(6_000_00, m.Available);
    }

    [Fact]
    public void Spending_reduces_item_remaining()
    {
        var snap = Snapshot(
            new[] { Tx(Cheque, D(1, 3), 0, -300_00, Petrol), Tx(Cheque, D(1, 5), 0, -250_00, Petrol) },
            allocations: new[] { new Allocation(BudgetEngine.FirstMonthId, Petrol, 1_000_00) });

        var petrol = BudgetEngine.Calculate(snap).Single().Items.Single(i => i.ItemId == Petrol);

        Assert.Equal(-550_00, petrol.Activity);
        Assert.Equal(450_00, petrol.Remaining);
    }

    [Fact]
    public void Transactions_after_the_marker_belong_to_the_new_month()
    {
        var marker = new MonthMarker(Guid.NewGuid(), D(1, 25), 0, "February 2027");
        var snap = Snapshot(
            new[]
            {
                Tx(Cheque, D(1, 24), 0, -100_00, Food),
                Tx(Cheque, D(1, 26), 0, -40_00, Food),
            },
            new[] { marker });

        var months = BudgetEngine.Calculate(snap);

        Assert.Equal(-100_00, Month(months, BudgetEngine.FirstMonthId).Items.Single(i => i.ItemId == Food).Activity);
        Assert.Equal(-40_00, Month(months, marker.Id).Items.Single(i => i.ItemId == Food).Activity);
    }

    [Fact]
    public void Dragging_the_marker_moves_transactions_between_months()
    {
        var txs = new[]
        {
            Tx(Cheque, D(1, 20), 0, -10_00, Food),
            Tx(Cheque, D(1, 24), 0, -20_00, Food),
            Tx(Cheque, D(1, 28), 0, -30_00, Food),
        };
        var id = Guid.NewGuid();

        long FirstMonthFood(MonthMarker marker) => Month(
            BudgetEngine.Calculate(Snapshot(txs, new[] { marker })), BudgetEngine.FirstMonthId)
            .Items.Single(i => i.ItemId == Food).Activity;

        Assert.Equal(-10_00, FirstMonthFood(new MonthMarker(id, D(1, 22), 0, "Feb")));
        Assert.Equal(-30_00, FirstMonthFood(new MonthMarker(id, D(1, 26), 0, "Feb")));
        Assert.Equal(-60_00, FirstMonthFood(new MonthMarker(id, D(2, 1), 0, "Feb")));
    }

    [Fact]
    public void Same_day_transaction_after_the_marker_is_in_the_new_month_and_before_it_is_not()
    {
        var marker = new MonthMarker(Guid.NewGuid(), D(1, 25), 5, "Feb");
        var markers = BudgetEngine.SortMarkers(new[] { marker });

        Assert.Equal(BudgetEngine.FirstMonthId, BudgetEngine.MonthOf(markers, D(1, 25), 4));
        Assert.Equal(marker.Id, BudgetEngine.MonthOf(markers, D(1, 25), 5));
        Assert.Equal(marker.Id, BudgetEngine.MonthOf(markers, D(1, 25), 6));
    }

    [Fact]
    public void Leftover_item_balance_returns_to_the_pool_as_overflow()
    {
        var marker = new MonthMarker(Guid.NewGuid(), D(2, 1), 0, "Feb");
        var snap = Snapshot(
            new[]
            {
                Tx(Cheque, D(1, 1), 0, 1_000_00, Salary),
                Tx(Cheque, D(1, 10), 0, -600_00, Food),
                Tx(Cheque, D(2, 1), 1, 1_000_00, Salary),
            },
            new[] { marker },
            new[]
            {
                new Allocation(BudgetEngine.FirstMonthId, Food, 1_000_00),
                new Allocation(marker.Id, Food, 1_400_00),
            });

        var months = BudgetEngine.Calculate(snap);

        Assert.Equal(0, months[0].Available);
        Assert.Equal(400_00, months[1].Overflow);
        Assert.Equal(0, months[1].Available);
    }

    [Fact]
    public void Overbudgeting_one_month_carries_into_the_next_as_a_negative()
    {
        var marker = new MonthMarker(Guid.NewGuid(), D(2, 1), 0, "Feb");
        var snap = Snapshot(
            new[]
            {
                Tx(Cheque, D(1, 1), 0, 1_000_00, Salary),
                Tx(Cheque, D(1, 2), 0, -1_007_00, Food),
                Tx(Cheque, D(2, 1), 1, 1_000_00, Salary),
            },
            new[] { marker },
            new[]
            {
                new Allocation(BudgetEngine.FirstMonthId, Food, 1_007_00),
                new Allocation(marker.Id, Food, 993_00),
            });

        var months = BudgetEngine.Calculate(snap);

        Assert.Equal(-7_00, months[0].Available);
        Assert.Equal(-7_00, months[1].CarriedOver);
        Assert.Equal(0, months[1].Available);
    }

    [Fact]
    public void Held_income_is_only_available_next_month()
    {
        var marker = new MonthMarker(Guid.NewGuid(), D(2, 1), 0, "Feb");
        var snap = Snapshot(
            new[] { Tx(Cheque, D(1, 25), 0, 10_000_00, Salary) },
            new[] { marker },
            holds: new[] { new IncomeHold(BudgetEngine.FirstMonthId, Salary, 4_000_00) });

        var months = BudgetEngine.Calculate(snap);

        Assert.Equal(10_000_00, months[0].Income);
        Assert.Equal(4_000_00, months[0].HeldOut);
        Assert.Equal(6_000_00, months[0].Available);
        Assert.Equal(4_000_00, months[1].HeldIn);
        Assert.Equal(10_000_00, months[1].Available);
    }

    [Fact]
    public void Income_items_report_expected_received_and_held_amounts_separately_from_expenses()
    {
        var snap = Snapshot(
            new[] { Tx(Cheque, D(1, 25), 0, 8_000_00, Salary), Tx(Cheque, D(1, 26), 0, -100_00, Food) },
            allocations: new[]
            {
                new Allocation(BudgetEngine.FirstMonthId, Salary, 9_000_00),
                new Allocation(BudgetEngine.FirstMonthId, Food, 500_00),
            },
            holds: new[] { new IncomeHold(BudgetEngine.FirstMonthId, Salary, 3_000_00) });

        var month = BudgetEngine.Calculate(snap).Single();

        var salary = Assert.Single(month.IncomeItems);
        Assert.Equal(9_000_00, salary.Expected);
        Assert.Equal(8_000_00, salary.Received);
        Assert.Equal(3_000_00, salary.Held);
        Assert.DoesNotContain(month.Items, i => i.ItemId == Salary);
        Assert.Equal(500_00, month.Budgeted);
        Assert.Equal(4_500_00, month.Available);
    }

    [Fact]
    public void Planned_months_follow_the_last_real_month_with_chained_labels_and_no_activity()
    {
        var marker = new MonthMarker(Guid.NewGuid(), D(2, 1), 0, "February 2027");
        var snap = Snapshot(new[] { Tx(Cheque, D(2, 3), 0, -100_00, Food) }, new[] { marker });

        var months = BudgetEngine.Calculate(snap, plannedMonths: 3);

        Assert.Equal(5, months.Count);
        Assert.Equal(new[] { false, false, true, true, true }, months.Select(m => m.IsPlanned));
        Assert.Equal(new[] { "March 2027", "April 2027", "May 2027" }, months.Skip(2).Select(m => m.Label));
        Assert.Equal(BudgetEngine.PlannedMonthId(1), months[2].MonthId);
        Assert.All(months.Skip(2), m => Assert.All(m.Items, i => Assert.Equal(0, i.Activity)));
    }

    [Fact]
    public void Budgeting_ahead_uses_leftovers_and_held_income_from_the_last_real_month()
    {
        var snap = Snapshot(
            new[] { Tx(Cheque, D(1, 25), 0, 10_000_00, Salary), Tx(Cheque, D(1, 26), 0, -300_00, Food) },
            allocations: new[]
            {
                new Allocation(BudgetEngine.FirstMonthId, Food, 1_000_00),
                new Allocation(BudgetEngine.PlannedMonthId(1), Food, 800_00),
                new Allocation(BudgetEngine.PlannedMonthId(2), Food, 900_00),
            },
            holds: new[] { new IncomeHold(BudgetEngine.FirstMonthId, Salary, 4_000_00) });

        var months = BudgetEngine.Calculate(snap, plannedMonths: 3);

        // January: income 10,000 less 4,000 held, less 1,000 budgeted. Unbudgeted 5,000 carries on.
        Assert.Equal(5_000_00, months[0].Available);
        // Next month has 4,000 released, 700 left over on Food and the 5,000 carried, less its 800 budget.
        Assert.Equal(4_000_00, months[1].HeldIn);
        Assert.Equal(700_00, months[1].Overflow);
        Assert.Equal(4_000_00 + 700_00 + 5_000_00 - 800_00, months[1].Available);
        Assert.Equal(800_00, months[1].Items.Single(i => i.ItemId == Food).Budgeted);
        Assert.Equal(900_00, months[2].Items.Single(i => i.ItemId == Food).Budgeted);
    }

    [Fact]
    public void Without_asking_for_planned_months_none_are_added()
    {
        Assert.Single(BudgetEngine.Calculate(Snapshot(Array.Empty<Transaction>())));
    }

    [Fact]
    public void Tracked_account_transactions_do_not_affect_the_budget()
    {
        var snap = Snapshot(
            new[]
            {
                Tx(Cheque, D(1, 1), 0, 5_000_00, Salary),
                Tx(Savings, D(1, 2), 0, 9_999_00, Salary),
                Tx(Savings, D(1, 3), 0, -500_00, Food),
            });

        var m = BudgetEngine.Calculate(snap).Single();

        Assert.Equal(5_000_00, m.Income);
        Assert.Equal(0, m.Items.Single(i => i.ItemId == Food).Activity);
    }

    [Fact]
    public void Transfer_between_budget_and_tracked_account_counts_as_spending_on_the_budget_side()
    {
        var snap = Snapshot(
            new[]
            {
                Tx(Cheque, D(1, 1), 0, 5_000_00, Salary),
                Tx(Cheque, D(1, 5), 0, -800_00, Food),   // budget side, assigned to an item
                Tx(Savings, D(1, 5), 0, 800_00, null),   // tracked side
            },
            allocations: new[] { new Allocation(BudgetEngine.FirstMonthId, Food, 1_000_00) });

        var food = BudgetEngine.Calculate(snap).Single().Items.Single(i => i.ItemId == Food);

        Assert.Equal(200_00, food.Remaining);
    }

    [Fact]
    public void Split_lines_are_applied_to_their_own_items_and_must_balance()
    {
        var split = new Transaction(
            Guid.NewGuid(), Cheque, D(1, 4), 0, -500_00,
            new[] { new SplitLine(Food, -350_00), new SplitLine(Petrol, -150_00) });

        var m = BudgetEngine.Calculate(Snapshot(new[] { split })).Single();

        Assert.True(split.IsBalanced);
        Assert.Equal(-350_00, m.Items.Single(i => i.ItemId == Food).Activity);
        Assert.Equal(-150_00, m.Items.Single(i => i.ItemId == Petrol).Activity);
        Assert.False(split with { Amount = -400_00 } is { IsBalanced: true });
    }
}
