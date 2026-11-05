using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

public sealed partial class AccountsPage : Page, IReloadable
{
    public AccountsViewModel ViewModel { get; } = new();

    public Task ReloadAsync() => ViewModel.LoadAsync();

    public AccountsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private async void Add_Click(object sender, RoutedEventArgs e) => await ViewModel.AddAsync();

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not { } row) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete {row.Name}?",
            Content = "An account that already has transactions is closed instead of deleted, so its history is kept.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.DeleteSelectedAsync();
    }
}
