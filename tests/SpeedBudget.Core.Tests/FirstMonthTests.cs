using System.Globalization;
using SpeedBudget.Core;

namespace SpeedBudget.Core.Tests;

public class FirstMonthTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly Guid Budget = Guid.NewGuid(), Tracked = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 11, 4);

    private static BudgetSnapshot Snapshot(string chosen = "", bool renamed = false, IEnumerable<MonthMarker>? markers = null,
        params (Guid Account, DateOnly Date)[] transactions) =>
        new(new[] { new Account(Budget, "Cheque", true), new Account(Tracked, "Retirement", false) },
            Array.Empty<BudgetItem>(),
            (markers ?? []).ToList(),
            transactions.Select((t, i) => new Transaction(Guid.NewGuid(), t.Account, t.Date, i, -10_00, Array.Empty<SplitLine>())).ToList(),
            Array.Empty<Allocation>(), Array.Empty<IncomeHold>(), chosen, renamed);

    private static string First(BudgetSnapshot s) => BudgetEngine.FirstMonthLabelFor(s, Today, Culture);

    [Fact]
    public void With_nothing_to_go_on_the_first_month_is_the_current_month() =>
        Assert.Equal("November 2026", First(Snapshot()));

    [Fact]
    public void The_month_chosen_in_the_wizard_is_the_first_month() =>
        Assert.Equal("March 2024", First(Snapshot("March 2024")));

    [Fact]
    public void The_first_budget_transaction_decides_the_first_month()
    {
        var s = Snapshot(transactions: [(Budget, new DateOnly(2025, 9, 20)), (Budget, new DateOnly(2025, 11, 2))]);
        Assert.Equal("September 2025", First(s));
    }

    [Fact]
    public void A_transaction_before_the_chosen_month_moves_the_first_month_back()
    {
        var s = Snapshot("March 2024", transactions: [(Budget, new DateOnly(2024, 1, 15))]);
        Assert.Equal("January 2024", First(s));
    }

    [Fact]
    public void Once_there_are_transactions_they_decide_the_first_month_not_the_setup_choice()
    {
        var s = Snapshot("March 2024", transactions: [(Budget, new DateOnly(2024, 5, 1))]);
        Assert.Equal("May 2024", First(s));
    }

    [Fact]
    public void A_stale_stored_month_never_forces_the_first_month()
    {
        // A stored "June 2025" (left over from an earlier session) must not hold the first month back once the lines say otherwise.
        var line = new MonthMarker(Guid.NewGuid(), new DateOnly(2025, 8, 1), 0, "August 2025");
        Assert.Equal("July 2025", First(Snapshot("June 2025", markers: [line])));
    }

    [Fact]
    public void Transactions_in_tracked_accounts_do_not_count()
    {
        var s = Snapshot("June 2025", transactions: [(Tracked, new DateOnly(2020, 1, 1))]);
        Assert.Equal("June 2025", First(s));
    }

    [Fact]
    public void A_month_line_dated_earlier_pulls_the_first_month_back_to_the_month_before_it()
    {
        var line = new MonthMarker(Guid.NewGuid(), new DateOnly(2026, 12, 1), 0, "December 2026");
        Assert.Equal("November 2026", First(Snapshot("January 2027", markers: [line])));
    }

    [Fact]
    public void Only_transactions_before_the_first_line_belong_to_the_first_month()
    {
        var line = new MonthMarker(Guid.NewGuid(), new DateOnly(2026, 6, 1), 0, "June 2026");
        var s = Snapshot(markers: [line], transactions: [(Budget, new DateOnly(2026, 7, 1))]);
        Assert.Equal("May 2026", First(s));   // the July transaction is after the line, so it says nothing about the first month
    }

    [Fact]
    public void A_name_typed_by_the_user_is_kept()
    {
        var s = Snapshot("Opening balances", renamed: true, transactions: [(Budget, new DateOnly(2020, 1, 1))]);
        Assert.Equal("Opening balances", First(s));
    }

    [Fact]
    public void Later_months_are_named_from_the_first_one()
    {
        var s = Snapshot("March 2024");
        var labels = BudgetEngine.Calculate(s, 2).Select(m => m.Label);
        Assert.Equal(new[] { "March 2024", "April 2024", "May 2024" }, labels);
    }
}
