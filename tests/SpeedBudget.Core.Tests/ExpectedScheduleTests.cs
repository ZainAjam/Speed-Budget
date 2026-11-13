using SpeedBudget.Core;

namespace SpeedBudget.Core.Tests;

public class ExpectedScheduleTests
{
    [Theory]
    [InlineData(ExpectedRepeat.Weekly, 2027, 1, 31, 31, 2027, 2, 7)]
    [InlineData(ExpectedRepeat.Fortnightly, 2027, 1, 31, 31, 2027, 2, 14)]
    [InlineData(ExpectedRepeat.Monthly, 2027, 1, 31, 31, 2027, 2, 28)]
    [InlineData(ExpectedRepeat.Monthly, 2027, 2, 28, 31, 2027, 3, 31)]
    [InlineData(ExpectedRepeat.Monthly, 2027, 12, 15, 15, 2028, 1, 15)]
    [InlineData(ExpectedRepeat.Quarterly, 2027, 11, 30, 30, 2028, 2, 29)]
    [InlineData(ExpectedRepeat.Yearly, 2028, 2, 29, 29, 2029, 2, 28)]
    [InlineData(ExpectedRepeat.Yearly, 2029, 2, 28, 29, 2030, 2, 28)]
    public void Next_date_keeps_to_the_day_of_the_month_it_started_on(ExpectedRepeat repeat, int y, int m, int d, int anchor, int ny, int nm, int nd) =>
        Assert.Equal(new DateOnly(ny, nm, nd), ExpectedSchedule.Next(new DateOnly(y, m, d), repeat, anchor));
}
