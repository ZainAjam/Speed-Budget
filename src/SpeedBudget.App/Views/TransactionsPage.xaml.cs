using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpeedBudget.App.ViewModels;
using SpeedBudget.Core;
using Windows.System;
using Windows.UI.Core;

namespace SpeedBudget.App.Views;

public sealed partial class TransactionsPage : Page, IReloadable
{
    public TransactionsViewModel ViewModel { get; } = new();

    public Task ReloadAsync() => ViewModel.LoadAsync();

    public TransactionsPage()
    {
        InitializeComponent();
        // Oldest first opens at the bottom of the table; the rows stay out of sight until they are there, so no scrolling shows.
        if (ViewModel.SortIndex == 1) EntryList.Opacity = 0;
        // Handled presses count too: the boxes and buttons in the table handle theirs.
        AddHandler(PointerPressedEvent, new PointerEventHandler(Page_PointerPressed), true);
        AddHandler(KeyDownEvent, new KeyEventHandler(Page_KeyDown), true);
        HeadingGrid.AddHandler(PointerPressedEvent, new PointerEventHandler(Heading_PointerPressed), true);
        WireFilters();
        Loaded += (_, _) => AppServices.ZoomChanged += OnZoomForRows;
        Unloaded += (_, _) => AppServices.ZoomChanged -= OnZoomForRows;
        StartMonthBox.ItemsSource = Enumerable.Range(1, 12).Select(m => System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)).ToList();
        ViewModel.AskMonthAsync = AskMonthAsync;
        ViewModel.AskStartEarlierAsync = AskStartEarlierAsync;
        Loaded += async (_, _) =>
        {
            await LoadLayoutAsync();
            Layout.PropertyChanged += Layout_Changed;
            UpdateTableWidth();
            UpdateDetailPanel();
            try { await ViewModel.LoadAsync(); await ScrollToLatestAsync(); }
            finally { EntryList.Opacity = 1; }
        };
        Unloaded += (_, _) => { Layout.PropertyChanged -= Layout_Changed; EditEscape.Pressed -= PickerEscape; };
        Loaded += (_, _) => { EditEscape.Pressed -= PickerEscape; EditEscape.Pressed += PickerEscape; };
        ViewModel.Expected.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExpectedViewModel.IsOpen)) _ = UpdateExpectedPanelAsync();
        };
        // Changing the sort order shows the end of the table that is now "latest" (the top for newest first, the bottom for oldest first),
        // already in place: the rows stay out of sight while they re-order, so no scrolling shows.
        ViewModel.SortReloaded += () => DispatcherQueue.TryEnqueue(async () =>
        {
            try { await ShowSortedEndAsync(); }
            finally { EntryList.Opacity = 1; }
        });
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TransactionsViewModel.SortIndex)) EntryList.Opacity = 0;
            if (e.PropertyName is nameof(TransactionsViewModel.HasMarkerSelected)) UpdateDetailPanel();
            if (e.PropertyName is nameof(TransactionsViewModel.SelectedMarker)) { ShowStartDate(); ShowMarkerDate(); }
        };
    }


    // ---- Expected transactions ----

    private int _expectedChange;

    /// <summary>Rises from the bar along the bottom when opened, and drops back out of sight when closed.</summary>
    private async Task UpdateExpectedPanelAsync()
    {
        var change = ++_expectedChange;
        if (ExpectedHost.TranslationTransition is null)
            ExpectedHost.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(200) };

        var hidden = new System.Numerics.Vector3(0, (float)ExpectedHost.Height + 40, 0);
        if (ViewModel.Expected.IsOpen)
        {
            ExpectedHost.Translation = hidden;
            ExpectedHost.Visibility = Visibility.Visible;
            await Task.Delay(30);   // let it show at the bottom first, so there is something to slide from
            if (change == _expectedChange) ExpectedHost.Translation = System.Numerics.Vector3.Zero;
        }
        else
        {
            ExpectedHost.Translation = hidden;
            await Task.Delay(220);
            if (change == _expectedChange) ExpectedHost.Visibility = Visibility.Collapsed;
        }
    }

    private void ExpectedToggle_Click(object sender, RoutedEventArgs e) => ViewModel.Expected.IsOpen = !ViewModel.Expected.IsOpen;

    private async void ExpectedSave_Click(object sender, RoutedEventArgs e) => await ViewModel.Expected.SaveFormAsync();

    private void ExpectedCancel_Click(object sender, RoutedEventArgs e) => ViewModel.Expected.CancelEdit();

    private void ExpectedEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ExpectedRow row) ViewModel.Expected.BeginEdit(row);
    }

    private async void ExpectedDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ExpectedRow row) await ViewModel.Expected.DeleteAsync(row);
    }

    /// <summary>"Happened": asks the date it happened, then records it in the transactions table and takes it off this list.</summary>
    private async void ExpectedHappened_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ExpectedRow row) return;
        var date = new CalendarDatePicker
        {
            Date = DateTimeOffset.Now, DateFormat = "{day.integer} {month.abbreviated} {year.full}", HorizontalAlignment = HorizontalAlignment.Stretch,
            Header = "Date it happened",
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "It happened",
            PrimaryButtonText = "Add to transactions",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 12, MinWidth = 340,
                Children =
                {
                    new TextBlock { Text = $"{row.PayeeText}   {row.OutflowText}{row.InflowText}", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "It is added to the transactions table on this date, and leaves the expected list.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8 },
                    date,
                },
            },
        };
        dialog.PrimaryButtonClick += (_, args) => { if (date.Date is null) args.Cancel = true; };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || date.Date is not { } picked) return;
        await ViewModel.Expected.RecordAsync(row, DateOnly.FromDateTime(picked.DateTime));
    }

    /// <summary>Esc inside a counterparty or budget item pop-up of the table: the row being edited is cancelled as well (the pop-up closes itself).</summary>
    private void PickerEscape(object picker)
    {
        if (picker is not DependencyObject element || !IsWithin(element, TableRegion) || ViewModel.Selected is null) return;
        DispatcherQueue.TryEnqueue(ClearEditing);
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

    /// <summary>
    /// Enter in any field of the row being edited saves the whole row and takes it out of edit mode; Esc drops every change to
    /// it (or the new row altogether) and takes it out of edit mode. A list that is open (a drop-down, a calendar) keeps its
    /// own use of these keys, and so does a picker button, where Enter opens the list.
    /// </summary>
    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Enter or VirtualKey.Escape) || ViewModel.Selected is null) return;

        // Esc works wherever the focus is on the page (even with a drop-down open): a new row is deleted, and a row being edited goes
        // back to the values it had before and leaves edit mode. The Expected transactions flyout keeps its own Esc.
        if (e.Key == VirtualKey.Escape)
        {
            if (FocusManager.GetFocusedElement(XamlRoot) is DependencyObject where && IsWithin(where, ExpectedHost)) return;
            e.Handled = true;
            ClearEditing();
            return;
        }

        if (FocusManager.GetFocusedElement(XamlRoot) is not DependencyObject focused || !IsWithin(focused, TableRegion)) return;

        var inField = focused is TextBox or CalendarDatePicker or CheckBox or ComboBox { IsDropDownOpen: false };
        if (!inField) return;
        e.Handled = true;
        EntryList.Focus(FocusState.Programmatic);   // the box hands over what was typed
        // A number box finishes reading its text a moment after it loses focus, so save once that has happened.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _ = ViewModel.CommitEditAsync());
    }

    /// <summary>A press anywhere outside the table (and the month line panel) lets go of the row that is selected. A row being edited is not let go of.</summary>
    private void Page_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // The Expected transactions flyout closes when something outside it (and its bar) is pressed.
        if (ViewModel.Expected.IsOpen && !IsWithin(e.OriginalSource as DependencyObject, ExpectedHost) && !IsWithin(e.OriginalSource as DependencyObject, ExpectedBar))
            ViewModel.Expected.IsOpen = false;
        if (ViewModel.SelectedEntry is null || ViewModel.IsRowEditing) return;   // a row being edited is only left with Enter, the green check or Esc
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

    // Diagnostics for column resizing: what is under the pointer when the table's heading is pressed, and what each drag did.
    // It is written to resize.log in the data folder, and keeps to the last few hundred lines.
    private static void Trace(string line)
    {
        try
        {
            var path = Path.Combine(AppServices.DataDirectory, "resize.log");
            if (File.Exists(path) && new FileInfo(path).Length > 60_000) File.Delete(path);
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch { /* diagnostics must never get in the way */ }
    }

    private void Heading_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            var point = e.GetCurrentPoint(HeadingGrid).Position;
            var under = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(e.GetCurrentPoint(null).Position, HeadingGrid)
                .Select(el => el is ResizeGrip g ? $"Grip[{g.Tag}]" : el.GetType().Name + (el is FrameworkElement { Tag: { } t } ? $"[{t}]" : ""));
            Trace($"heading press x={point.X:0.#} source={(e.OriginalSource as FrameworkElement)?.GetType().Name} under=[{string.Join(", ", under)}] showItem={Layout.ShowItem} showAccount={Layout.ShowAccount}");
        }
        catch { }
    }

    private static string ColumnOf(object? grip) => (string)((FrameworkElement)grip!).Tag;

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
        Trace($"grip start {ColumnOf(sender)} width={_gripStart} table={TableRegion.ActualWidth:0.#} widths={Layout.Serialize()}");
        // Memo may be wider than its own width because it takes the spare room; a drag starts from what is on screen.
        var memoOnScreen = Math.Floor(TableRegion.ActualWidth - (Layout.FixedTotal - Layout.Memo));   // rounded down, so the columns never add up to more than the room there is
        if (memoOnScreen > Layout.Memo) Layout.Set(nameof(ColumnLayout.Memo), memoOnScreen);
        _gripColumns = ColumnsOnShow();
        _gripWidths = _gripColumns.ToDictionary(c => c, Layout.Get);
        _gripFillerStart = Math.Max(ColumnLayout.ActionsMinimum, TableRegion.ActualWidth - (Layout.FixedTotal - ColumnLayout.ActionsMinimum));
    }

    private void Grip_Moved(object? sender, double dx)
    {
        try { ResizeFromGrip(ColumnOf(sender), dx); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Resizing a column failed: {ex.Message}"); }
    }

    private void ResizeFromGrip(string column, double dx)
    {
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

        var isLast = index == _gripColumns.Count - 1;
        if (shift || (isLast && dx >= 0))
        {
            widths[column] = _gripWidths[column] + dx;   // the last column has no neighbour to trade with, so the table grows
        }
        else if (dx >= 0)
        {
            var room = 0.0;
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
            for (var j = isLast ? index : 0; j <= index; j++) room += _gripWidths[_gripColumns[j]] - ColumnLayout.MinimumOf(_gripColumns[j]);
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
        Trace($"grip done {ColumnOf(sender)} width={Layout.Get(ColumnOf(sender))}");
        await ViewModel.SaveColumnLayoutAsync(Layout.Serialize());
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

    // ---- Crisp lines when the table is zoomed ----
    // Each row is made just tall enough (by less than a pixel) to end on a whole screen pixel at the current zoom, so the lines between
    // rows are all the same thickness instead of some falling across two pixels.

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ListViewItem, object> _snappedRows = new();

    private void EntryList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not ListViewItem item || _snappedRows.TryGetValue(item, out _)) return;
        _snappedRows.Add(item, new object());
        item.SizeChanged += (_, _) => SnapRow(item);
    }

    private static void SnapRow(ListViewItem item)
    {
        if (item.ContentTemplateRoot is not FrameworkElement root) return;
        var zoom = AppServices.Zoom;
        var natural = root.DesiredSize.Height;
        if (natural <= 0) return;
        var wanted = Math.Abs(zoom - 1) < 0.0001 ? 0 : Math.Ceiling(natural * zoom - 0.001) / zoom;
        if (Math.Abs(item.MinHeight - wanted) > 0.001) item.MinHeight = wanted;
    }

    /// <summary>The zoom changed: every row works out its height again.</summary>
    private void OnZoomForRows() => DispatcherQueue.TryEnqueue(() => { UpdateTableWidth(); SnapAllRows(); });

    private void SnapAllRows()
    {
        if (EntryList.ItemsPanelRoot is not Panel panel) return;
        foreach (var item in panel.Children.OfType<ListViewItem>())
        {
            item.MinHeight = 0;
            item.InvalidateMeasure();
        }
    }

    private void TableScroll_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTableWidth();

    private void UpdateTableWidth()
    {
        var width = Math.Max(TableScroll.ActualWidth, Layout.FixedTotal);
        // Zoomed, the width is nudged (by less than a pixel) so the Memo column ends on a whole screen pixel, and so do the columns after it.
        var zoom = AppServices.Zoom;
        if (Math.Abs(zoom - 1) > 0.0001 && width > ColumnLayout.ActionsMinimum) width = Math.Round((width - ColumnLayout.ActionsMinimum) * zoom) / zoom + ColumnLayout.ActionsMinimum;
        TableRegion.Width = width;
        TableFrame.BorderThickness = new Thickness(Math.Abs(zoom - 1) < 0.0001 ? 1 : 1 / zoom);   // one screen pixel, so the table's edge is as crisp as at 100%
    }

    /// <summary>The navigation parameter is the account to show, or nothing for all accounts.</summary>
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        ViewModel.AccountId = e.Parameter is Guid id ? id : null;
        ViewModel.ShowBehind(e.Parameter as TransactionDrillDown);
    }

    /// <summary>Right-click (or the menu key) on a transaction row: edit it or delete it.</summary>
    private void Row_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: TransactionRow row } target) return;
        args.Handled = true;
        if (row.IsNew) return;   // a row still being added has its own tick and bin

        var menu = new MenuFlyout();
        var edit = new MenuFlyoutItem { Text = "Edit", Icon = new FontIcon { Glyph = "\uE70F" }, IsEnabled = !row.IsEditing };
        edit.Click += (_, _) => ViewModel.BeginEdit(row);
        var delete = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "\uE74D" } };
        delete.Click += async (_, _) => await DeleteRowAsync(row);
        menu.Items.Add(edit);
        menu.Items.Add(delete);

        if (args.TryGetPosition(target, out var point)) menu.ShowAt(target, point);
        else menu.ShowAt(target);
    }

    /// <summary>A budget item typed into the drop-down that doesn't exist: ask for its category, create it, and select it.</summary>
    private async void Item_CreateRequested(object? sender, string name)
    {
        var groups = await AppServices.Store.GetGroupsAsync();
        if (groups.Count == 0)
        {
            ViewModel.Message = "Add a budget group first: use the + beside Cash Outflow on the budget screen.";
            return;
        }
        var dialog = new NewItemDialog(groups, name) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var option = await ViewModel.CreateItemAsync(dialog.GroupId, dialog.ItemName);
        if (option is not null && sender is ItemPicker picker) picker.SelectedItem = option;
    }

    /// <summary>The pencil beside a counterparty in the drop-down: edit its name and icon.</summary>
    private async void Payee_EditRequested(object? sender, string name)
    {
        var dialog = new PayeeEditDialog(name, ViewModel.PayeeIcons.GetValueOrDefault(name, "")) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var error = await ViewModel.EditPayeeAsync(name, dialog.PayeeName, dialog.Emoji);
        if (error is not null) ViewModel.Message = error;
        else (sender as PayeePicker)?.RefreshSelection();
    }

    /// <summary>The bin beside a counterparty in the drop-down: delete it, after asking. One that transactions use can't be deleted.</summary>
    private async void Payee_DeleteRequested(object? sender, string name)
    {
        var uses = await ViewModel.CountPayeeUsesAsync(name);
        if (uses > 0)
        {
            ViewModel.Message = $"{name} is used on {uses} transaction{(uses == 1 ? "" : "s")}, so it can't be deleted. Rename it with the pencil, or change those transactions first.";
            return;
        }
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = $"Delete {name}?", Content = "The counterparty is removed from the list.",
            PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var error = await ViewModel.DeletePayeeAsync(name);
        if (error is not null) ViewModel.Message = error;
        else (sender as PayeePicker)?.RefreshSelection();
    }

    /// <summary>The pencil beside a budget item in the drop-down: edit the item and its group.</summary>
    private async void Item_EditRequested(object? sender, ItemOption option)
    {
        if (option.Id is not { } itemId) return;
        var item = (await AppServices.Store.GetItemsAsync()).FirstOrDefault(i => i.Id == itemId);
        if (item is null) return;
        var groups = (await AppServices.Store.GetGroupsAsync()).Where(g => g.Kind == item.Kind).ToList();

        var dialog = new ItemEditDialog(groups, item.Name, item.Emoji ?? "", item.GroupId) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var error = await ViewModel.EditItemAsync(itemId, dialog.ItemName, dialog.Emoji, dialog.GroupId, dialog.GroupName);
        if (error is not null) ViewModel.Message = error;
    }

    /// <summary>A payee typed into the drop-down that was not in the list: save it so it is there from now on.</summary>
    private async void Payee_CreateRequested(object? sender, string name) => await ViewModel.CreatePayeeAsync(name);

    /// <summary>The pencil on a row: turns edit mode on for it.</summary>
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TransactionRow row) ViewModel.BeginEdit(row);
    }

    /// <summary>The tick on the row being edited: saves it and takes it out of edit mode, like Enter.</summary>
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TransactionRow row || !ReferenceEquals(row, ViewModel.Selected)) return;
        EntryList.Focus(FocusState.Programmatic);   // the box being typed in hands over what was typed
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _ = ViewModel.CommitEditAsync());
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var row = (sender as FrameworkElement)?.DataContext as TransactionRow ?? ViewModel.Selected;
        if (row is not null) await DeleteRowAsync(row);
    }

    private async Task DeleteRowAsync(TransactionRow row)
    {
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
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.DeleteAsync(row);
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
            if (row.Entity.Amount == 0 || row.AmountFromSplit)
            {
                // No amount typed: the lines come first, and the transaction's total is worked out from them.
                IReadOnlyList<(Guid? ItemId, long Amount, string? Memo)> lines0 = row.DraftSplits
                    ?? (row.Item.Id is { } first ? new[] { ((Guid?)first, 0L, (string?)null) } : Array.Empty<(Guid?, long, string?)>());
                dialog = new SplitDialog(ViewModel, "Split between items   ·   the total is worked out from the lines", 0, row.Entity.Amount > 0 ? 1 : -1,
                    ViewModel.RequiresItem(row), lines0, lines => { row.SetSplitFromLines(lines); return Task.FromResult<string?>(null); }, openEnded: true);
            }
            else
            {
                var total = Math.Abs(row.Entity.Amount);
                IReadOnlyList<(Guid? ItemId, long Amount, string? Memo)> existing = row.DraftSplits
                    ?? (row.Item.Id is { } id ? new[] { ((Guid?)id, row.Entity.Amount, (string?)null) } : Array.Empty<(Guid?, long, string?)>());
                dialog = new SplitDialog(ViewModel, $"Split between items   ·   total {AppServices.ToRands(total):N2}", total, row.Entity.Amount < 0 ? -1 : 1,
                    ViewModel.RequiresItem(row), existing, lines => { row.SetDraftSplits(lines); return Task.FromResult<string?>(null); });
            }
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

    /// <summary>
    /// Oldest first puts what was added last at the bottom: bring it into view, so the user can see it (and add the next one)
    /// without scrolling down. Rows that are not measured yet make one jump fall short, so it goes again until the last row is in view.
    /// </summary>
    private async Task ShowSortedEndAsync()
    {
        if (ViewModel.SortIndex == 1) { await ScrollToLatestAsync(); return; }
        if (ViewModel.Rows.Count == 0) return;
        EntryList.ScrollIntoView(ViewModel.Rows[0]);
        await Task.Delay(30);
        if (FindScrollViewer(EntryList) is { } viewer) viewer.ChangeView(null, 0, null, disableAnimation: true);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer viewer) return viewer;
            if (FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }

    private async Task ScrollToLatestAsync()
    {
        if (ViewModel.SortIndex != 1) return;
        for (var pass = 0; pass < 12 && ViewModel.Rows.Count > 0; pass++)
        {
            var last = ViewModel.Rows[^1];
            EntryList.ScrollIntoView(last);
            await Task.Delay(30);
            if (EntryList.ContainerFromItem(last) is FrameworkElement row
                && row.TransformToVisual(EntryList).TransformPoint(new Windows.Foundation.Point(0, row.ActualHeight)).Y <= EntryList.ActualHeight + 1)
                return;
        }
    }

    /// <summary>Clicking a split row's budget item (when it is not being edited) shows what the split is made of, in a pop-up over it.</summary>
    private async void ItemName_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TransactionRow { IsSplit: true, IsEditing: false } row } anchor) return;
        e.Handled = true;

        var lines = await ViewModel.GetLinesAsync(row);
        var panel = new StackPanel { Spacing = 6, MinWidth = 260 };
        panel.Children.Add(new TextBlock { Text = $"Split into {lines.Count} lines", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        foreach (var line in lines)
        {
            var label = ViewModel.ItemOptions.FirstOrDefault(o => o.Id == line.BudgetItemId)?.Label ?? "No budget item";
            var grid = new Grid { ColumnSpacing = 24 };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis };
            var amount = new TextBlock { Text = Format.Money(Math.Abs(line.Amount)), HorizontalTextAlignment = TextAlignment.Right };
            Grid.SetColumn(amount, 1);
            grid.Children.Add(name);
            grid.Children.Add(amount);
            panel.Children.Add(grid);
        }
        panel.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 2, 0, 2), Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"] });
        var total = new Grid { ColumnSpacing = 24 };
        total.ColumnDefinitions.Add(new ColumnDefinition());
        total.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var totalLabel = new TextBlock { Text = "Total", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
        var totalAmount = new TextBlock { Text = Format.Money(Math.Abs(lines.Sum(l => l.Amount))), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], HorizontalTextAlignment = TextAlignment.Right };
        Grid.SetColumn(totalAmount, 1);
        total.Children.Add(totalLabel);
        total.Children.Add(totalAmount);
        panel.Children.Add(total);

        new Flyout { Content = panel, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom }.ShowAt(anchor);
    }

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
