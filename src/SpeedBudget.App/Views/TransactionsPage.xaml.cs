using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpeedBudget.App.ViewModels;
using SpeedBudget.Core;
using Windows.System;
using Windows.UI.Core;

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
        StartMonthBox.ItemsSource = Enumerable.Range(1, 12).Select(m => System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)).ToList();
        ViewModel.AskMonthAsync = AskMonthAsync;
        ViewModel.AskStartEarlierAsync = AskStartEarlierAsync;
        Loaded += async (_, _) =>
        {
            await LoadLayoutAsync();
            Layout.PropertyChanged += Layout_Changed;
            UpdateTableWidth();
            UpdateDetailPanel();
            await ViewModel.LoadAsync();
        };
        TableScroll.ViewChanged += TableScroll_ViewChanged;
        Unloaded += (_, _) => Layout.PropertyChanged -= Layout_Changed;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TransactionsViewModel.HasMarkerSelected)) UpdateDetailPanel();
            if (e.PropertyName is nameof(TransactionsViewModel.SelectedMarker)) { ShowStartDate(); ShowMarkerDate(); }
        };
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

    // While a column is being dragged the table must not slide sideways: the scroll position is held where it was, and the
    // table is not allowed to get narrower (which would push the scroll position back) until the drag ends.
    private bool _tableGripActive;
    private double _gripScroll;

    // The widths of every column on show, and the spare room at the end of the table, when the drag began.
    private List<string> _gripColumns = [];
    private Dictionary<string, double> _gripWidths = [];
    private double _gripFillerStart;

    /// <summary>The columns on show, left to right.</summary>
    private List<string> ColumnsOnShow()
    {
        var order = new List<string> { nameof(ColumnLayout.Date), nameof(ColumnLayout.BankDate), nameof(ColumnLayout.Counterparty), nameof(ColumnLayout.Memo) };
        if (Layout.ShowItem) order.Add(nameof(ColumnLayout.Item));
        if (Layout.ShowAccount) order.Add(nameof(ColumnLayout.Account));
        order.AddRange([nameof(ColumnLayout.Outflow), nameof(ColumnLayout.Inflow), nameof(ColumnLayout.Cleared)]);
        return order;
    }

    private void Grip_Started(object? sender, EventArgs e)
    {
        _gripStart = Layout.Get(ColumnOf(sender));
        if (ColumnOf(sender) == nameof(ColumnLayout.DetailWidth)) return;
        _gripColumns = ColumnsOnShow();
        _gripWidths = _gripColumns.ToDictionary(c => c, Layout.Get);
        _gripFillerStart = Math.Max(ColumnLayout.ActionsMinimum, TableRegion.ActualWidth - (Layout.FixedTotal - ColumnLayout.ActionsMinimum));
        _gripScroll = TableScroll.HorizontalOffset;
        TableRegion.MinWidth = TableRegion.ActualWidth;
        _tableGripActive = true;
    }

    private void TableScroll_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_tableGripActive && Math.Abs(TableScroll.HorizontalOffset - _gripScroll) > 0.5)
            TableScroll.ChangeView(_gripScroll, null, null, disableAnimation: true);
    }

    /// <summary>The drag is over: the table may take the width its columns need again.</summary>
    private void EndTableGrip()
    {
        if (!_tableGripActive) return;
        _tableGripActive = false;
        TableRegion.MinWidth = 0;
        UpdateTableWidth();
    }

    private void Grip_Moved(object? sender, double dx)
    {
        var column = ColumnOf(sender);
        if (column == nameof(ColumnLayout.DetailWidth))
        {
            Layout.Set(column, _gripStart - dx);   // the side panel's edge is on its left, so dragging left widens it
            return;
        }

        // As in Word: the divider moves, and the columns either side of it give and take room, so the table keeps its width and its
        // place. Dragging right, the column on the left grows and the ones to its right shrink, nearest first, each down to its
        // minimum. Dragging left, the columns on the left shrink (this one first, then those before it) and the one on the right
        // grows. With Shift held, the column on the left changes on its own and everything to its right moves along.
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        var index = _gripColumns.IndexOf(column);
        if (index < 0) return;
        var widths = new Dictionary<string, double>(_gripWidths);

        if (shift)
        {
            widths[column] = _gripWidths[column] + dx;
        }
        else if (dx >= 0)
        {
            var room = _gripFillerStart - ColumnLayout.ActionsMinimum;
            for (var j = index + 1; j < _gripColumns.Count; j++) room += _gripWidths[_gripColumns[j]] - ColumnLayout.MinimumOf(_gripColumns[j]);
            var amount = Math.Min(Math.Min(dx, room), 600 - _gripWidths[column]);
            widths[column] = _gripWidths[column] + amount;
            for (var j = index + 1; j < _gripColumns.Count && amount > 0; j++)
            {
                var name = _gripColumns[j];
                var take = Math.Min(amount, _gripWidths[name] - ColumnLayout.MinimumOf(name));
                widths[name] = _gripWidths[name] - take;
                amount -= take;
            }
        }
        else
        {
            var room = 0.0;
            for (var j = 0; j <= index; j++) room += _gripWidths[_gripColumns[j]] - ColumnLayout.MinimumOf(_gripColumns[j]);
            var amount = Math.Min(-dx, room);
            if (index + 1 < _gripColumns.Count) amount = Math.Min(amount, 600 - _gripWidths[_gripColumns[index + 1]]);
            if (index + 1 < _gripColumns.Count) widths[_gripColumns[index + 1]] = _gripWidths[_gripColumns[index + 1]] + amount;
            for (var j = index; j >= 0 && amount > 0; j--)
            {
                var name = _gripColumns[j];
                var take = Math.Min(amount, _gripWidths[name] - ColumnLayout.MinimumOf(name));
                widths[name] = _gripWidths[name] - take;
                amount -= take;
            }
        }
        foreach (var (name, width) in widths) Layout.Set(name, width);
    }

    private async void Grip_Completed(object? sender, EventArgs e)
    {
        EndTableGrip();
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

    // ---- The date the budget starts on ----

    private bool _showingStart;

    /// <summary>Shows the budget's start date and first month in the boxes when the first-month row is selected.</summary>
    private void ShowStartDate()
    {
        if (ViewModel.SelectedMarker is not { IsFirst: true, StartDate: { } start, StartMonth: { } month }) return;
        _showingStart = true;
        StartDateBox.Date = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue));
        StartMonthBox.SelectedIndex = month.Month - 1;
        StartYearBox.Value = month.Year;
        _showingStart = false;
    }

    private async void StartDate_Changed(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_showingStart || args.NewDate is not { } picked) return;
        if (ViewModel.SelectedMarker is not { IsFirst: true, StartDate: { } current, StartMonth: { } currentMonth }) return;
        var date = DateOnly.FromDateTime(picked.DateTime);
        if (date == current) return;

        // While the first month simply follows the date, it keeps following it; once the user has chosen another month, that stays.
        var follows = currentMonth.Year == current.Year && currentMonth.Month == current.Month;
        await ViewModel.SetBudgetStartAsync(date, follows ? new DateOnly(date.Year, date.Month, 1) : currentMonth);
    }

    private async void StartMonth_Changed(object sender, SelectionChangedEventArgs e) => await ApplyStartMonthAsync();

    private async void StartYear_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args) => await ApplyStartMonthAsync();

    private async Task ApplyStartMonthAsync()
    {
        if (_showingStart || StartMonthBox.SelectedIndex < 0 || double.IsNaN(StartYearBox.Value)) return;
        if (ViewModel.SelectedMarker is not { IsFirst: true, StartDate: { } start, StartMonth: { } current }) return;
        var month = new DateOnly((int)StartYearBox.Value, StartMonthBox.SelectedIndex + 1, 1);
        if (month == current) return;
        await ViewModel.SetBudgetStartAsync(start, month);
    }

    /// <summary>
    /// Something was dated before the budget starts. Offers to start the budget from that date's month, which adds the
    /// months in between to the budget.
    /// </summary>
    private async Task<bool> AskStartEarlierAsync(DateOnly date, DateOnly start)
    {
        var month = MonthLabels.For(date);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Before the start of your budget",
            Content = $"This is dated {date:d MMM yyyy}, but your budget starts on {start:d MMM yyyy}. " +
                      $"Start the budget from {month} instead? The months in between are added to your budget, and nothing already in it changes.",
            PrimaryButtonText = $"Start from {month}",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void TableScroll_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTableWidth();

    private void UpdateTableWidth()
    {
        TableRegion.Width = Math.Max(TableScroll.ActualWidth, Layout.FixedTotal);   // the edit panel floats over the table, so it needs no room of its own
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
        if (!ViewModel.CanStartMonth) return;   // a tracked account is outside the budget
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
