using SpeedBudget.Core;

namespace SpeedBudget.Core.Tests;

public class ReconciliationTests
{
    [Fact]
    public void Available_to_spend_is_the_statement_plus_uncleared_transactions_as_in_the_sheet()
    {
        var result = Reconciliation.Calculate(0, new[] { new ReconcileLine(-475_68, false) }, 9_054_75);

        Assert.Equal(-475_68, result.UnclearedTotal);
        Assert.Equal(8_579_07, result.AvailableToSpend);
    }

    [Fact]
    public void Account_is_reconciled_when_the_cleared_balance_matches_the_statement()
    {
        var lines = new[]
        {
            new ReconcileLine(5_000_00, true),
            new ReconcileLine(-1_200_00, true),
            new ReconcileLine(-300_00, false),
        };

        var result = Reconciliation.Calculate(1_000_00, lines, 4_800_00);

        Assert.Equal(4_500_00, result.BookBalance);
        Assert.Equal(4_800_00, result.ClearedBalance);
        Assert.Equal(0, result.Difference);
        Assert.True(result.IsReconciled);
    }

    [Fact]
    public void Difference_shows_how_far_the_statement_is_from_the_cleared_balance()
    {
        var result = Reconciliation.Calculate(0, new[] { new ReconcileLine(100_00, true) }, 130_00);

        Assert.Equal(30_00, result.Difference);
        Assert.False(result.IsReconciled);
    }

    [Fact]
    public void Without_a_statement_balance_there_is_nothing_to_compare()
    {
        var result = Reconciliation.Calculate(500_00, new[] { new ReconcileLine(-50_00, false) }, null);

        Assert.Null(result.Difference);
        Assert.Null(result.AvailableToSpend);
        Assert.False(result.IsReconciled);
        Assert.Equal(450_00, result.BookBalance);
    }
}
