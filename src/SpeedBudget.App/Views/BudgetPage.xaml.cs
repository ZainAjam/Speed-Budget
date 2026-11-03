using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SpeedBudget.App.Views;

public sealed partial class BudgetPage : Page
{
    public BudgetPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var firstRun = await AppServices.Store.IsEmptyAsync() && !FirstRunDismissed.Value;
        Welcome.Visibility = firstRun ? Visibility.Visible : Visibility.Collapsed;
        NextSteps.Visibility = firstRun ? Visibility.Collapsed : Visibility.Visible;
        if (firstRun) return;

        var accounts = await AppServices.Store.GetAccountsAsync();
        var items = await AppServices.Store.GetItemsAsync();
        Summary.Text = $"{accounts.Count} account(s) and {items.Count} budget item(s) are set up.";
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

    private void Accounts_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(AccountsPage));

    private void Items_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ItemsPage));
}

/// <summary>Remembers, for this run, that the user chose to start empty.</summary>
internal static class FirstRunDismissed
{
    public static bool Value { get; set; }
}
