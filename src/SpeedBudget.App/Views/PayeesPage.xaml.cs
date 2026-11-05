using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

public sealed partial class PayeesPage : Page, IReloadable
{
    public PayeesViewModel ViewModel { get; } = new();

    public Task ReloadAsync() => ViewModel.LoadAsync();

    public PayeesPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync(selectName: _openName);
    }

    private string? _openName;

    /// <summary>The navigation parameter, when there is one, is the name of the counterparty to open for editing.</summary>
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e) =>
        _openName = e.Parameter as string;

    private async void Add_Click(object sender, RoutedEventArgs e) => await ViewModel.AddAsync();

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not { } row) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete {row.Name}?",
            Content = "A counterparty that transactions already use can't be deleted.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.DeleteSelectedAsync();
    }
}
