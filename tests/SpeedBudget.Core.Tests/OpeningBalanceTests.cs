using SpeedBudget.Core;

namespace SpeedBudget.Core.Tests;

public class OpeningBalanceTests
{
    private static readonly Guid Cheque = Guid.NewGuid(), Savings = Guid.NewGuid(), Retirement = Guid.NewGuid();
    private static readonly Guid Marker = Guid.NewGuid();

    private static BudgetSnapshot Snapshot() => new(
        new[] { new Account(Cheque, "Cheque", true, 5_000_00), new Account(Savings, "Savings", true, 1_500_00), new Account(Retirement, "Retirement", false, 900_000_00) },
        Array.Empty<BudgetItem>(),
        new[] { new MonthMarker(Marker, new DateOnly(2026, 12, 1), 0, "December 2026") },
        Array.Empty<Transaction>(), Array.Empty<Allocation>(), Array.Empty<IncomeHold>(), "November 2026");

    [Fact]
    public void The_opening_balances_of_budget_accounts_are_income_of_the_first_month_only()
    {
        var months = BudgetEngine.Calculate(Snapshot());
        Assert.Equal(6_500_00, months[0].Income);
        Assert.Equal(6_500_00, months[0].OpeningBalances);
        Assert.Equal(6_500_00, months[0].Available);   // nothing is budgeted yet, so all of it is available
        Assert.Equal(0, months[1].Income);
        Assert.Equal(6_500_00, months[1].Available);   // and it carries over to the next month
    }

    [Fact]
    public void A_tracked_accounts_opening_balance_is_not_income()
    {
        var snapshot = Snapshot() with { Accounts = new[] { new Account(Retirement, "Retirement", false, 900_000_00) } };
        Assert.Equal(0, BudgetEngine.Calculate(snapshot)[0].Income);
    }
}
