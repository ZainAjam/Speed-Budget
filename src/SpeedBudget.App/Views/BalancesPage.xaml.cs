using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SpeedBudget.App.ViewModels;
using Windows.System;

namespace SpeedBudget.App.Views;

/// <summary>An account kept by its balances (see <see cref="BalancesViewModel"/>), shown in place of the transactions table.</summary>
public sealed partial class BalancesPage : Page, IReloadable
{
    public BalancesViewModel ViewModel { get; } = new();

    public BalancesPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is Guid accountId) await ViewModel.LoadAsync(accountId);
    }

    public Task ReloadAsync() => ViewModel.AccountId == Guid.Empty ? Task.CompletedTask : ViewModel.LoadAsync(ViewModel.AccountId);

    private void Daily_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowMonths = false;
        DailyButton.IsChecked = true;
        MonthButton.IsChecked = false;
    }

    private void Month_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowMonths = true;
        MonthButton.IsChecked = true;
        DailyButton.IsChecked = false;
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await ViewModel.SaveAsync();

    private async void CancelChange_Click(object sender, RoutedEventArgs e) => await ViewModel.CancelChangeAsync();

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BalanceLine line }) ViewModel.BeginChange(line);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BalanceLine line }) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete this balance?",
            Content = $"The balance on {line.DateText} ({line.BalanceText}) is removed, and the changes, gains and losses are worked out again without it.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.DeleteAsync(line);
    }

    /// <summary>Enter adds the balance (or saves the change), once the box being typed in has taken what was typed.</summary>
    private void Form_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        DispatcherQueue.TryEnqueue(async () => await ViewModel.SaveAsync());
    }
}
