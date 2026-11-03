using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

public sealed partial class BudgetPage : Page
{
    public BudgetViewModel ViewModel { get; } = new();

    public BudgetPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var firstRun = await AppServices.Store.IsEmptyAsync() && !FirstRunDismissed.Value;
        Welcome.Visibility = firstRun ? Visibility.Visible : Visibility.Collapsed;
        BudgetContent.Visibility = firstRun ? Visibility.Collapsed : Visibility.Visible;
        if (firstRun) return;

        await ViewModel.LoadAsync();
        EmptyHint.Visibility = ViewModel.HasItems ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Starter_Click(object sender, RoutedEventArgs e)
    {
        await AppServices.Store.SeedStarterItemsAsync();
        await RefreshAsync();
    }

    private async void Empty_Click(object sender, RoutedEventArgs e)
    {
        FirstRunDismissed.Value = true;
        await RefreshAsync();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ViewModel.GoBack();

    private void Forward_Click(object sender, RoutedEventArgs e) => ViewModel.GoForward();

    private async void HoldAll_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is IncomeRow row) await ViewModel.HoldAllAsync(row);
    }

    private async void CopyLast_Click(object sender, RoutedEventArgs e) => await ViewModel.CopyLastMonthAsync();

    private async void SpentLast_Click(object sender, RoutedEventArgs e) => await ViewModel.BudgetLastMonthsSpendingAsync();
}

/// <summary>Remembers, for this run, that the user chose to start empty.</summary>
internal static class FirstRunDismissed
{
    public static bool Value { get; set; }
}
