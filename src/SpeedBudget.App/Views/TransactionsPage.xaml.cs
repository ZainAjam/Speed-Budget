using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpeedBudget.App.ViewModels;
using Windows.System;

namespace SpeedBudget.App.Views;

public sealed partial class TransactionsPage : Page
{
    public TransactionsViewModel ViewModel { get; } = new();

    public TransactionsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private void Payee_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = ViewModel.SuggestPayees(sender.Text);
    }

    private void Payee_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string payee) ViewModel.ApplyHint(payee);
    }

    private void Payee_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        ViewModel.ApplyHint(args.QueryText);

    private async void Add_Click(object sender, RoutedEventArgs e) => await AddAndRefocusAsync();

    // Enter commits a NumberBox's text, so it is handled on key-up to see the committed value.
    private async void Entry_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) await AddAndRefocusAsync();
    }

    private async Task AddAndRefocusAsync()
    {
        if (await ViewModel.AddAsync()) PayeeBox.Focus(FocusState.Programmatic);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not { } row) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete this transaction?",
            Content = row.IsTransfer ? "This is one side of a transfer, so both sides will be deleted." : row.Payee,
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.DeleteSelectedAsync();
    }

    private async void Transfer_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TransferDialog(ViewModel) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }
}
