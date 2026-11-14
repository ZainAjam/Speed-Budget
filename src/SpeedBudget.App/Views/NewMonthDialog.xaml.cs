using System.Globalization;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;
using SpeedBudget.Core;

namespace SpeedBudget.App.Views;

/// <summary>
/// Starts a new month. The user only says which calendar month it is for; the line goes at the end of the list and has no date; its name is then
/// that month and year (such as "March 2027"), never typed.
/// </summary>
public sealed partial class NewMonthDialog : ContentDialog
{
    private int _month;
    private int _year;

    /// <summary>The new month's name: the chosen month and year.</summary>
    public string MonthName => MonthLabels.For(new DateOnly(_year, _month, 1));
    public DateOnly Month => new(_year, _month, 1);

    public NewMonthDialog(TransactionsViewModel vm)
    {
        InitializeComponent();

        // Start from the month after the last one.
        var suggested = vm.SuggestMonthLabel(DateTimeOffset.Now);
        var first = MonthLabels.TryParse(suggested, out var parsed) ? parsed : new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        _month = first.Month;
        _year = first.Year;

        MonthBox.ItemsSource = Enumerable.Range(1, 12).Select(m => CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)).ToList();
        MonthBox.SelectedIndex = _month - 1;
        YearBox.Value = _year;
        ShowName();
    }

    private void MonthBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonthBox.SelectedIndex >= 0) _month = MonthBox.SelectedIndex + 1;
        ShowName();
    }

    private void YearBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!double.IsNaN(sender.Value)) _year = (int)Math.Clamp(sender.Value, 1950, 2100);
        ShowName();
    }

    private void ShowName() => Preview.Text = $"The new month will be called {MonthName}.";
}
