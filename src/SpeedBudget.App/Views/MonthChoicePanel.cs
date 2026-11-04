using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.Core;

namespace SpeedBudget.App.Views;

/// <summary>One month a transaction dated on a month line's date could belong to, and where in that day it must sit to be in it.</summary>
public sealed record MonthChoice(string Label, int DayOrder);

/// <summary>
/// A transaction dated on the same day as a "(month) starts here" line could be in the month that ends that day or in the
/// month that starts that day. This panel appears when that is the case and asks which month and year it belongs to.
/// It stays hidden for any other date.
/// </summary>
public sealed class MonthChoicePanel : UserControl
{
    private readonly RadioButtons _choices = new();
    private IReadOnlyList<MonthChoice> _options = [];

    /// <summary>The position within the day that puts the transaction in the month the user chose, or null when nothing needed asking.</summary>
    public int? SelectedDayOrder => Visibility == Visibility.Visible && _choices.SelectedIndex >= 0 ? _options[_choices.SelectedIndex].DayOrder : null;

    public MonthChoicePanel()
    {
        Visibility = Visibility.Collapsed;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        var text = new TextBlock
        {
            Text = "A month starts on this date. Which month does this transaction belong to?",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        };
        Content = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(6),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorAttentionBackgroundBrush"],
            Child = new StackPanel { Spacing = 6, Children = { text, _choices } },
        };
    }

    /// <summary>Looks at the chosen date and shows or hides the question.</summary>
    public async Task ShowForAsync(DateOnly date)
    {
        var options = await MonthLineChoices.ForDateAsync(date);
        _options = options;
        if (options.Count == 0)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        _choices.Items.Clear();
        foreach (var option in options) _choices.Items.Add(option.Label);
        _choices.SelectedIndex = options.Count - 1;   // by default it goes in the month that starts that day, as it always did
        Visibility = Visibility.Visible;
    }
}

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
