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
    public void A_stored_month_is_ignored_while_there_are_no_budget_transactions() =>
        Assert.Equal("November 2026", First(Snapshot("March 2024")));

    [Fact]
    public void The_month_given_for_the_first_transaction_is_the_first_month()
    {
        // The transaction is dated in May, but the user said it belongs to March.
        var s = Snapshot("March 2024", transactions: [(Budget, new DateOnly(2024, 5, 1))]);
        Assert.Equal("March 2024", First(s));
    }

    [Fact]
    public void Without_an_answer_the_first_budget_transaction_decides_the_first_month()
    {
        var s = Snapshot(transactions: [(Budget, new DateOnly(2025, 9, 20)), (Budget, new DateOnly(2025, 11, 2))]);
        Assert.Equal("September 2025", First(s));
    }

    [Fact]
    public void Transactions_in_tracked_accounts_do_not_count()
    {
        var s = Snapshot("June 2025", transactions: [(Tracked, new DateOnly(2020, 1, 1))]);
        Assert.Equal("November 2026", First(s));
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
        var s = Snapshot("March 2024", transactions: [(Budget, new DateOnly(2024, 3, 9))]);
        var labels = BudgetEngine.Calculate(s, 2, today: new DateOnly(2024, 3, 20)).Select(m => m.Label);
        Assert.Equal(new[] { "March 2024", "April 2024", "May 2024" }, labels);
    }
}
