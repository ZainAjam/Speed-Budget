using SpeedBudget.Core;

namespace SpeedBudget.App.Views;

/// <summary>One month a transaction dated on a month line's date could belong to, and where in that day it must sit to be in it.</summary>
public sealed record MonthChoice(string Label, int DayOrder);

internal static class MonthLineChoices
{
    /// <summary>The months a transaction on this date could be in, oldest first, or nothing if no month line sits on that date.</summary>
    public static async Task<IReadOnlyList<MonthChoice>> ForDateAsync(DateOnly date)
    {
        var store = AppServices.Store;
        var snapshot = await store.LoadSnapshotAsync();
        var lines = BudgetEngine.SortMarkers(snapshot.Markers);
        var onDate = lines.Where(m => m.Date == date).ToList();
        if (onDate.Count == 0) return [];

        var firstIndex = lines.ToList().FindIndex(m => m.Id == onDate[0].Id);
        var before = firstIndex > 0 ? lines[firstIndex - 1].Label : BudgetEngine.FirstMonthLabelFor(snapshot);

        // Before the first line of the day: in the month that ends there.
        var choices = new List<MonthChoice> { new(before, onDate[0].DayOrder - 1) };

        // After each line: in that line's month. After the last one it goes to the end of the day.
        for (var i = 0; i < onDate.Count; i++)
        {
            var order = i == onDate.Count - 1 ? await store.NextDayOrderAsync(date) : onDate[i].DayOrder;
            choices.Add(new MonthChoice(onDate[i].Label, order));
        }
        return choices;
    }
}
