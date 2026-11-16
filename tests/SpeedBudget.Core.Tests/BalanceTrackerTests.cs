using SpeedBudget.Core;

namespace SpeedBudget.Core.Tests;

public class BalanceTrackerTests
{
    private static BalanceInput Entry(int month, int day, long balance, long withdrawal = 0) =>
        new(Guid.CreateVersion7(), new DateOnly(2024, month, day), balance, withdrawal, null);

    [Fact]
    public void Follows_the_spreadsheet_for_changes_withdrawals_and_gains()
    {
        // The first lines of the "Detailed" sheet (amounts in cents).
        var report = BalanceTracker.Calculate(new[]
        {
            Entry(1, 29, 158_253_955),
            Entry(1, 30, 158_031_621),
            Entry(1, 31, 158_275_918),
            Entry(2, 1, 157_082_987, withdrawal: 870_000),
        });

        Assert.Equal(new long[] { 0, -222_334, 21_963, -1_170_968 }, report.Rows.Select(r => r.TotalChange));
        Assert.Equal(new long[] { 0, -222_334, 244_297, -1_192_931 }, report.Rows.Select(r => r.DailyChange));
        Assert.Equal(new long[] { 0, -222_334, 244_297, -322_931 }, report.Rows.Select(r => r.GainLoss));
        Assert.Equal(-1_170_968, report.Totals.DailyChanges);
        Assert.Equal(870_000, report.Totals.Withdrawals);
        Assert.Equal(-222_334 + 244_297 - 322_931, report.Totals.GainsLosses);
    }

    [Fact]
    public void Months_add_up_gains_and_withdrawals_and_say_what_is_left_to_reinvest()
    {
        var report = BalanceTracker.Calculate(new[]
        {
            Entry(1, 29, 1_000_000),
            Entry(1, 31, 1_020_000),                              // +20 000 in January
            Entry(2, 1, 250_000, withdrawal: 870_000),             // the balance fell by 770 000, of which 870 000 was withdrawn: the market added 100 000
        });

        Assert.Equal(new[] { "2024-01", "2024-02" }, report.Months.Select(m => m.Month));
        var jan = report.Months[0];
        Assert.Equal(20_000, jan.GainsLosses);
        Assert.Equal(0, jan.Withdrawals);
        Assert.Equal(20_000, jan.LeftToReinvest);
        var feb = report.Months[1];
        Assert.Equal(100_000, feb.GainsLosses);
        Assert.Equal(-870_000, feb.Withdrawals);
        Assert.Equal(-770_000, feb.LeftToReinvest);
    }

    [Fact]
    public void Entries_are_put_in_date_order_and_nothing_gives_nothing()
    {
        Assert.Empty(BalanceTracker.Calculate([]).Rows);

        var report = BalanceTracker.Calculate(new[] { Entry(3, 2, 500), Entry(3, 1, 400) });
        Assert.Equal(new long[] { 400, 500 }, report.Rows.Select(r => r.Balance));
        Assert.Equal(100, report.Rows[1].DailyChange);
    }
}
