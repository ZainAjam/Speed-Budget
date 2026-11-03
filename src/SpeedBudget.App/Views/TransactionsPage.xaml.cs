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

    /// <summary>The navigation parameter is the account to show, or nothing for all accounts.</summary>
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e) =>
        ViewModel.AccountId = e.Parameter is Guid id ? id : null;

    private async void Reconcile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ReconcileDialog(ViewModel, await ViewModel.ClearedBalanceAsync(), await ViewModel.LastBankBalanceAsync()) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private async void Unreconcile_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is { } row) await ConfirmUnreconcileAsync(row);
    }

    /// <summary>The padlock on a reconciled row: after confirming, the row goes back to a ticked Cleared box.</summary>
    private async void Padlock_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TransactionRow row) await ConfirmUnreconcileAsync(row);
    }

    private async Task ConfirmUnreconcileAsync(TransactionRow row)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Unreconcile this transaction?",
            Content = $"{row.DateText}  {row.Payee}\r\n\r\nIt will go back to being cleared, and you will be able to change it. " +
                      "Your account may no longer match the bank until you reconcile again.",
            PrimaryButtonText = "Unreconcile",
            CloseButtonText = "Keep it locked",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.UnreconcileAsync(row);
    }

    /// <summary>Right-click (or the menu key) on a counterparty in the table: jump to its edit screen.</summary>
    private void Counterparty_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        // A transfer's name ("Transfer to ...") is not a counterparty, so there is nothing to edit.
        if (sender is not FrameworkElement { DataContext: TransactionRow { IsTransfer: false } row } target || string.IsNullOrWhiteSpace(row.Payee)) return;

        var menu = new MenuFlyout();
        var edit = new MenuFlyoutItem { Text = "Edit Counterparty" };
        edit.Click += (_, _) => (App.MainWindow as MainWindow)?.NavigateTo("payees", row.Payee);
        menu.Items.Add(edit);

        if (args.TryGetPosition(target, out var point)) menu.ShowAt(target, point);
        else menu.ShowAt(target);
        args.Handled = true;
    }

    /// <summary>A budget item typed into the drop-down that doesn't exist: ask for its category, create it, and select it.</summary>
    private async void Item_CreateRequested(object? sender, string name)
    {
        var groups = await AppServices.Store.GetGroupsAsync();
        if (groups.Count == 0)
        {
            ViewModel.Message = "Add a budget category first under Budget Items.";
            return;
        }
        var dialog = new NewItemDialog(groups, name) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var option = await ViewModel.CreateItemAsync(dialog.GroupId, dialog.ItemName);
        if (option is not null && sender is ItemPicker picker) picker.SelectedItem = option;
    }

    /// <summary>A payee typed into the drop-down that was not in the list: save it so it is there from now on.</summary>
    private async void Payee_CreateRequested(object? sender, string name) => await ViewModel.CreatePayeeAsync(name);

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

    private async void Split_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not { } row) return;
        var dialog = new SplitDialog(ViewModel, row, await ViewModel.GetLinesAsync(row)) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private async void Transfer_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TransferDialog(ViewModel) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private async void NewTransaction_Click(object sender, RoutedEventArgs e) => await NewTransactionAsync();

    /// <summary>Ctrl+N: open the new transaction dialog.</summary>
    private async void NewTransaction_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await NewTransactionAsync();
    }

    private async Task NewTransactionAsync() => await new NewTransactionDialog(ViewModel) { XamlRoot = XamlRoot }.ShowAsync();

    private async void NewMonth_Click(object sender, RoutedEventArgs e) => await StartNewMonthAsync();

    private async void NewMonth_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await StartNewMonthAsync();
    }

    private async Task StartNewMonthAsync()
    {
        var dialog = new NewMonthDialog(ViewModel) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.AddMonthAsync(dialog.MonthName, dialog.StartDate);
    }

    // Only a month line may be dragged; transactions stay where their date puts them.
    private void List_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count != 1 || e.Items[0] is not MarkerRow { CanDrag: true }) e.Cancel = true;
    }

    private async void List_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult != Windows.ApplicationModel.DataTransfer.DataPackageOperation.None
            && args.Items.Count == 1 && args.Items[0] is MarkerRow marker)
            await ViewModel.MarkerDroppedAsync(marker);
    }

    private async void MoveLineUp_Click(object sender, RoutedEventArgs e) => await MoveSelectedLineAsync(-1);

    private async void MoveLineDown_Click(object sender, RoutedEventArgs e) => await MoveSelectedLineAsync(1);

    private async void MoveLineUp_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await MoveSelectedLineAsync(-1);
    }

    private async void MoveLineDown_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await MoveSelectedLineAsync(1);
    }

    private async Task MoveSelectedLineAsync(int delta)
    {
        if (ViewModel.SelectedMarker is { } marker) await ViewModel.MoveMarkerByRowAsync(marker, delta);
    }

    private async void DeleteLine_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedMarker is not { IsFirst: false } marker) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete the line for {marker.Label}?",
            Content = "Its transactions move into the month before. The budgeted amounts of the two months are added together.",
            PrimaryButtonText = "Delete line",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.DeleteSelectedMarkerAsync();
    }
}
