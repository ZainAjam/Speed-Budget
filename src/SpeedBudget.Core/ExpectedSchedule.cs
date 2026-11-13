namespace SpeedBudget.Core;

/// <summary>How an expected transaction turns into a real one.</summary>
public enum ExpectedKind
{
    /// <summary>Expected some time, no date: it is recorded by hand when it happens.</summary>
    Someday = 0,

    /// <summary>Expected on one future date: it is recorded automatically on that date.</summary>
    Once = 1,

    /// <summary>Expected on a repeating date: it is recorded automatically on each of those dates.</summary>
    Repeating = 2,
}

/// <summary>How often a repeating expected transaction comes round.</summary>
public enum ExpectedRepeat
{
    Weekly = 0,
    Fortnightly = 1,
    Monthly = 2,
    Quarterly = 3,
    Yearly = 4,
}

public static class ExpectedSchedule
{
    public static string Describe(ExpectedRepeat repeat) => repeat switch
    {
        ExpectedRepeat.Weekly => "Every week",
        ExpectedRepeat.Fortnightly => "Every two weeks",
        ExpectedRepeat.Monthly => "Every month",
        ExpectedRepeat.Quarterly => "Every three months",
        _ => "Every year",
    };

    /// <summary>
    /// The date after <paramref name="date"/> in a repeating series. Monthly, quarterly and yearly series keep to the day of the
    /// month they started on (<paramref name="anchorDay"/>), using the last day of a shorter month: the 31st goes 31 Jan, 28 Feb, 31 Mar.
    /// </summary>
    public static DateOnly Next(DateOnly date, ExpectedRepeat repeat, int anchorDay)
    {
        switch (repeat)
        {
            case ExpectedRepeat.Weekly: return date.AddDays(7);
            case ExpectedRepeat.Fortnightly: return date.AddDays(14);
        }
        var months = repeat switch { ExpectedRepeat.Monthly => 1, ExpectedRepeat.Quarterly => 3, _ => 12 };
        var first = new DateOnly(date.Year, date.Month, 1).AddMonths(months);
        var day = Math.Min(anchorDay <= 0 ? date.Day : anchorDay, DateTime.DaysInMonth(first.Year, first.Month));
        return new DateOnly(first.Year, first.Month, day);
    }
}
