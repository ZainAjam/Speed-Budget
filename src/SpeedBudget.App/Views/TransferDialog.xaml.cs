using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;
using SpeedBudget.Core;

namespace SpeedBudget.App.Views;

/// <summary>
/// Between two accounts of the same kind the budget is untouched. When exactly one is a budget account,
/// the transfer counts as spending or income and needs a budget item of the matching kind.
/// </summary>
public sealed partial class TransferDialog : ContentDialog
{
    private readonly TransactionsViewModel _vm;

    public TransferDialog(TransactionsViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        FromBox.ItemsSource = vm.EntryAccounts;
        ToBox.ItemsSource = vm.EntryAccounts;
        DateBox.Date = DateTimeOffset.Now;
        AmountBox.Value = double.NaN;
        if (vm.EntryAccounts.Count > 0) FromBox.SelectedIndex = 0;
    }

    private void Accounts_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var from = FromBox.SelectedItem as AccountOption;
        var to = ToBox.SelectedItem as AccountOption;
        if (from is null || to is null || from.IsOnBudget == to.IsOnBudget)
        {
            ItemBox.Visibility = ItemHelp.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            return;
        }

        var kind = from.IsOnBudget ? ItemKind.Expense : ItemKind.Income;
        ItemBox.ItemsSource = _vm.ItemOptions.Where(o => o.Kind == kind).ToList();
        ItemBox.SelectedItem = null;
        ItemBox.Header = from.IsOnBudget ? "Expense item to count this against" : "Income item to count this as";
        ItemHelp.Text = from.IsOnBudget
            ? "Money moving to a tracked account counts as spending from your budget."
            : "Money moving in from a tracked account counts as income in your budget.";
        ItemBox.Visibility = ItemHelp.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
    }

    private async void Add_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Hold the dialog open until the store has accepted the transfer.
        var deferral = args.GetDeferral();
        var error = await _vm.AddTransferAsync(
            FromBox.SelectedItem as AccountOption, ToBox.SelectedItem as AccountOption,
            DateBox.Date ?? DateTimeOffset.Now, AmountBox.Value, MemoBox.Text, ItemBox.SelectedItem);
        Error.Text = error ?? "";
        args.Cancel = error is not null;
        deferral.Complete();
    }
}
