using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpeedBudget.App.ViewModels;
using SpeedBudget.Core;
using Windows.System;

namespace SpeedBudget.App.Views;

public sealed partial class TransactionsPage : Page
{
    public TransactionsViewModel ViewModel { get; } = new();

    public TransactionsPage()
    {
        InitializeComponent();
        // Handled presses count too: the boxes and buttons in the table handle theirs.
        AddHandler(PointerPressedEvent, new PointerEventHandler(Page_PointerPressed), true);
        AddHandler(KeyDownEvent, new KeyEventHandler(Page_KeyDown), true);
        ViewModel.AskMonthAsync = AskMonthAsync;
        Loaded += async (_, _) =>
        {
            await LoadLayoutAsync();
            Layout.PropertyChanged += Layout_Changed;
            UpdateTableWidth();
            UpdateDetailPanel();
            await ViewModel.LoadAsync();
        };
        Unloaded += (_, _) => Layout.PropertyChanged -= Layout_Changed;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TransactionsViewModel.HasMarkerSelected)) UpdateDetailPanel();
            if (e.PropertyName is nameof(TransactionsViewModel.SelectedMarker)) { ShowFirstMonth(); ShowMarkerDate(); }
        };
        FirstMonthBox.ItemsSource = Enumerable.Range(1, 12).Select(m => System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)).ToList();
    }


    private bool DetailOpen => ViewModel.HasMarkerSelected;   // only month lines use the side panel; transactions are edited in their rows

    /// <summary>Slides the edit panel in from the right edge, or back out of sight when nothing is selected.</summary>
    private void UpdateDetailPanel()
    {
        if (DetailHost.TranslationTransition is null)
            DetailHost.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(180) };

        // Closed, it waits just off the right edge; it takes no clicks, so the table underneath works as normal.
        DetailHost.Translation = DetailOpen ? System.Numerics.Vector3.Zero : new System.Numerics.Vector3((float)(Layout.DetailWidth + 40), 0, 0);
        DetailHost.IsHitTestVisible = DetailOpen;
    }

    private void CloseDetail_Click(object sender, RoutedEventArgs e) => ClearEditing();

    /// <summary>Esc: let go of whatever is being edited, and close the month line panel if it is open.</summary>
    private void CloseDetail_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.SelectedEntry is null) return;   // leave Esc alone when there is nothing to let go of
        args.Handled = true;
        ClearEditing();
    }

    /// <summary>
    /// Esc or a click away: takes the focus off any box in the table and puts the row back to how it was before it was
    /// edited. Focus moves first so the box lets go of what was typed, and then everything typed is discarded.
    /// </summary>
    private void ClearEditing()
    {
        EntryList.Focus(FocusState.Programmatic);
        _ = ViewModel.CancelEditAsync();
    }

    /// <summary>Enter in a text or number box: save the row's edits and finish editing it.</summary>
    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || ViewModel.Selected is null) return;
        if (FocusManager.GetFocusedElement(XamlRoot) is not TextBox box || !IsWithin(box, TableRegion)) return;
        e.Handled = true;
        EntryList.Focus(FocusState.Programmatic);   // the box hands over what was typed
        // A number box finishes reading its text a moment after it loses focus, so save once that has happened.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _ = ViewModel.CommitEditAsync());
    }

    /// <summary>A press anywhere outside the table (and the month line panel) lets go of the row being edited.</summary>
    private void Page_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.SelectedEntry is null) return;
        var source = e.OriginalSource as DependencyObject;
        if (IsWithin(source, TableRegion) || IsWithin(source, DetailHost)) return;
        ClearEditing();
    }

    private static bool IsWithin(DependencyObject? element, DependencyObject container)
    {
        for (var node = element; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (node == container) return true;
        return false;
    }

    // ---- Resizing columns and the side panel ----

    /// <summary>The shared widths of the table's columns and the edit panel.</summary>
    public ColumnLayout Layout => ColumnLayout.Current;

    private static bool s_layoutLoaded;
    private double _gripStart;

    private void Layout_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        UpdateTableWidth();
        if (e.PropertyName == nameof(ColumnLayout.DetailWidth)) UpdateDetailPanel();
    }

    private async Task LoadLayoutAsync()
    {
        if (s_layoutLoaded) return;
        s_layoutLoaded = true;
        try { Layout.Load(await AppServices.Store.GetLocalSettingAsync("txColumns")); }
        catch { /* the normal sizes are fine */ }
    }

    private static string ColumnOf(object? grip) => (string)((FrameworkElement)grip!).Tag;

    private void Grip_Started(object? sender, EventArgs e)
    {
        _gripStart = Layout.Get(ColumnOf(sender));
    }

    private void Grip_Moved(object? sender, double dx)
    {
        var column = ColumnOf(sender);
        // Columns right of Counterparty and the side panel are resized from their left edge, so dragging left widens them.
        if (column is nameof(ColumnLayout.Date) or nameof(ColumnLayout.BankDate)) Layout.Set(column, _gripStart + dx);
        else Layout.Set(column, _gripStart - dx);
    }

    private async void Grip_Completed(object? sender, EventArgs e)
    {
        try { await AppServices.Store.SetLocalSettingAsync("txColumns", Layout.Serialize()); }
        catch { /* not remembering the sizes must never get in the way */ }
    }

    // ---- The date a month line sits at ----

    private bool _showingMarkerDate;

    private void ShowMarkerDate()
    {
        if (ViewModel.SelectedMarker is not { Entity: { } line }) return;
        _showingMarkerDate = true;
        MarkerDateBox.Date = new DateTimeOffset(line.Date.ToDateTime(TimeOnly.MinValue));
        _showingMarkerDate = false;
    }

    private async void MarkerDate_Changed(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_showingMarkerDate || args.NewDate is not { } date) return;
        await ViewModel.MoveSelectedMarkerToDateAsync(DateOnly.FromDateTime(date.DateTime));
    }

    // ---- Choosing which month the first month is ----

    private bool _showingFirstMonth;

    /// <summary>Fills the month and year boxes from the selected first-month row.</summary>
    private void ShowFirstMonth()
    {
        if (ViewModel.SelectedMarker is not { IsFirst: true } row || !MonthLabels.TryParse(row.Label, out var month)) return;
        _showingFirstMonth = true;
        FirstMonthBox.SelectedIndex = month.Month - 1;
        FirstYearBox.Value = month.Year;
        _showingFirstMonth = false;
    }

    private async void FirstMonth_Changed(object sender, SelectionChangedEventArgs e) => await ApplyFirstMonthAsync();

    private async void FirstYear_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args) => await ApplyFirstMonthAsync();

    private async Task ApplyFirstMonthAsync()
    {
        if (_showingFirstMonth || FirstMonthBox.SelectedIndex < 0 || double.IsNaN(FirstYearBox.Value)) return;
        if (ViewModel.SelectedMarker is not { IsFirst: true } row) return;

        var month = new DateOnly((int)FirstYearBox.Value, FirstMonthBox.SelectedIndex + 1, 1);
        if (MonthLabels.For(month) == row.Label) return;   // nothing changed
        await ViewModel.SetFirstMonthAsync(month.Year, month.Month);
    }

    private void TableScroll_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTableWidth();

    private void UpdateTableWidth()
    {
        var minimum = Layout.FixedTotal + ColumnLayout.CounterpartyMinimum;   // the edit panel floats over the table, so it needs no room of its own
        TableRegion.Width = Math.Max(TableScroll.ActualWidth, minimum);
    }

    /// <summary>The navigation parameter is the account to show, or nothing for all accounts.</summary>
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e) =>
        ViewModel.AccountId = e.Parameter is Guid id ? id : null;

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
        if (row.IsNew) { ClearEditing(); return; }   // nothing saved yet: just drop it
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

    /// <summary>The Split row of a transaction's budget item drop-down, or the button on a split transaction: edit its lines.</summary>
    private async void Item_SplitRequested(object? sender, EventArgs e) => await SplitAsync(sender);

    private async void Split_Click(object sender, RoutedEventArgs e) => await SplitAsync(sender);

    private async Task SplitAsync(object? sender)
    {
        if ((sender as FrameworkElement)?.DataContext is not TransactionRow row) return;
        SplitDialog dialog;
        if (row.IsNew)
        {
            // Not saved yet: the lines are kept on the row and saved with it.
            if (row.Entity.Amount == 0) { ViewModel.Message = "Type the amount first, then split it between items."; return; }
            var total = Math.Abs(row.Entity.Amount);
            IReadOnlyList<(Guid? ItemId, long Amount, string? Memo)> existing = row.DraftSplits
                ?? (row.Item.Id is { } id ? new[] { ((Guid?)id, row.Entity.Amount, (string?)null) } : Array.Empty<(Guid?, long, string?)>());
            dialog = new SplitDialog(ViewModel, $"Split between items   ·   total {AppServices.ToRands(total):N2}", total, row.Entity.Amount < 0 ? -1 : 1,
                ViewModel.RequiresItem(row), existing, lines => { row.SetDraftSplits(lines); return Task.FromResult<string?>(null); });
        }
        else
        {
            await row.CommitAsync();   // the split saves straight away, so save the other edits first
            dialog = SplitDialog.ForRow(ViewModel, row, await ViewModel.GetLinesAsync(row));
        }
        dialog.XamlRoot = XamlRoot;
        await dialog.ShowAsync();
    }

    private void Transfer_Click(object sender, RoutedEventArgs e) => NewRow(transfer: true);

    private void NewTransaction_Click(object sender, RoutedEventArgs e) => NewRow(transfer: false);

    /// <summary>Ctrl+N: add a new transaction row.</summary>
    private void NewTransaction_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        NewRow(transfer: false);
    }

    /// <summary>Adds an editable row as the latest record and brings it into view, ready for the counterparty.</summary>
    private async void NewRow(bool transfer)
    {
        if (ViewModel.BeginNew(transfer) is not { } row) return;
        EntryList.ScrollIntoView(row);
        await Task.Delay(120);   // the row's boxes exist once the list has laid it out
        if (EntryList.ContainerFromItem(row) is DependencyObject container && FindFirst<PayeePicker>(container) is { } picker && !transfer) picker.FocusPicker();
    }

    private static T? FindFirst<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (FindFirst<T>(child) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>
    /// A new row dated on a month line's date could be in the month that ends that day or the one that starts it, so ask
    /// which. Nothing is asked for any other date.
    /// </summary>
    private async Task<(bool Ok, int? DayOrder)> AskMonthAsync(DateOnly date)
    {
        var options = await MonthLineChoices.ForDateAsync(date);
        if (options.Count == 0) return (true, null);

        var choices = new RadioButtons();
        foreach (var option in options) choices.Items.Add(option.Label);
        choices.SelectedIndex = options.Count - 1;   // by default it goes in the month that starts that day
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Which month?",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "A month starts on this date. Which month does this transaction belong to?", TextWrapping = TextWrapping.Wrap },
                    choices,
                },
            },
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? (true, options[choices.SelectedIndex].DayOrder) : (false, null);
    }

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
