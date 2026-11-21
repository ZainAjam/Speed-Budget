using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using SpeedBudget.App.ViewModels;
using SpeedBudget.Core;
using SpeedBudget.Data;
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
        // The side panel starts out of sight: the page now shows before its layout is read, and an empty panel must not flash.
        DetailHost.Translation = new System.Numerics.Vector3(4000, 0, 0);
        DetailHost.IsHitTestVisible = false;
        ViewModel.TickedChanged += UpdateTicks;
        ViewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TransactionsViewModel.IsSelecting)) { UpdateTicks(null, EventArgs.Empty); ShowSelectButtons(ViewModel.IsSelecting); } };
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
        Loaded += async (_, _) =>
        {
            // The account's name, badge and figures are quick, so they load at once; the page shows (frame, headings, buttons, the loading ring) and
            // only then are the transactions loaded into the table, which is the one part that can take long.
            var header = ViewModel.LoadHeaderAsync();
            await LoadingOverlay.WaitForFirstFrameAsync();
            if (!IsLoaded) return;   // the user moved on to another page meanwhile
            try { await header; } catch (Exception ex) { Diagnostics.Note($"could not load the account header: {ex.Message}"); }
            await LoadLayoutAsync();
            Layout.PropertyChanged += Layout_Changed;
            UpdateTableWidth();
            UpdateDetailPanel();
            try { await ViewModel.LoadAsync(); await ScrollToLatestAsync(); }
            finally { EntryList.Opacity = 1; await TableLoading.HideAsync(); }
        };
        Unloaded += (_, _) => { Layout.PropertyChanged -= Layout_Changed; EditEscape.Pressed -= PickerEscape; };
        Loaded += (_, _) => { EditEscape.Pressed -= PickerEscape; EditEscape.Pressed += PickerEscape; };
        SizeChanged += (_, e) => ApplyPageSize(e.NewSize.Width);
        // A round button at the bottom centre of a long table jumps to the end where the latest row is.
        JumpButton.Attach(EntryList, () => ViewModel.SortIndex == 0, () => ViewModel.Rows.Count);
        ViewModel.Rows.CollectionChanged += (_, _) => JumpButton.Refresh();
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
            if (e.PropertyName is nameof(TransactionsViewModel.SelectedMarker))
            {
                // Choosing a different line closes the panel until the line is clicked; reloading the same line after a save leaves it be.
                var key = ViewModel.SelectedMarker is { } marker ? marker.Entity?.Id ?? Guid.Empty : (Guid?)null;
                if (key != _panelFor) { _panelFor = key; _panelRequested = false; UpdateDetailPanel(); }
                ShowMarkerMonth();
            }
        };
    }



    /// <summary>Esc inside a counterparty or budget item pop-up of the table: the row being edited is cancelled as well (the pop-up closes itself).</summary>
    private void PickerEscape(object picker)
    {
        if (picker is not DependencyObject element || !IsWithin(element, TableRegion) || !ViewModel.IsRowEditing) return;
        var row = RowOf(element);
        DispatcherQueue.TryEnqueue(() => { if (row is { IsEditing: true }) _ = ViewModel.CancelRowAsync(row); else ClearEditing(); });
    }

    // A press on a month line selects it, and so does the start of a drag; the panel should only come out for a click, which is
    // what Tapped is (it does not fire when the press became a drag).
    private bool _panelRequested;
    private Guid? _panelFor;

    private void Marker_Tapped(object sender, TappedRoutedEventArgs e)
    {
        _panelRequested = true;
        UpdateDetailPanel();
    }

    private bool DetailOpen => ViewModel.HasMarkerSelected && _panelRequested;   // only month lines use the side panel; transactions are edited in their rows

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
        if (ViewModel.IsSelecting && !ViewModel.IsRowEditing) { ViewModel.IsSelecting = false; args.Handled = true; return; }   // Esc leaves multi-select mode
        if (ViewModel.SelectedEntry is null && !ViewModel.IsRowEditing) return;   // leave Esc alone when there is nothing to let go of
        args.Handled = true;
        ClearEditing();
    }

    /// <summary>
    /// Esc or a click away: takes the focus off any box in the table and puts the row back to how it was before it was
    /// edited. Focus moves first so the box lets go of what was typed, and then everything typed is discarded.
    /// </summary>
    private void ClearEditing()
    {
        // The row that holds the focus is the one that is dropped (Esc in one of several rows being edited drops only that row);
        // with the focus elsewhere, every row being edited is dropped. The focus is read first, because moving it ends the typing.
        var focused = RowOf(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject);
        EntryList.Focus(FocusState.Programmatic);
        if (focused is { IsEditing: true }) _ = ViewModel.CancelRowAsync(focused);
        else _ = ViewModel.CancelEditAsync();
    }

    /// <summary>The transaction row an element of the table belongs to, if any.</summary>
    private static TransactionRow? RowOf(DependencyObject? element)
    {
        for (var node = element; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (node is FrameworkElement { DataContext: TransactionRow row }) return row;
        return null;
    }

    /// <summary>
    /// Enter in any field of the row being edited saves the whole row and takes it out of edit mode; Esc drops every change to
    /// it (or the new row altogether) and takes it out of edit mode. A list that is open (a drop-down, a calendar) keeps its
    /// own use of these keys, and so does a picker button, where Enter opens the list.
    /// </summary>
    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && ViewModel.IsSelecting && !ViewModel.IsRowEditing) { e.Handled = true; ViewModel.IsSelecting = false; return; }   // Esc leaves multi-select mode
        if (e.Key is not (VirtualKey.Enter or VirtualKey.Escape) || (ViewModel.Selected is null && !ViewModel.IsRowEditing)) return;

        // Esc works wherever the focus is on the page (even with a drop-down open): a new row is deleted, and a row being edited goes
        // back to the values it had before and leaves edit mode.
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            ClearEditing();
            return;
        }

        if (FocusManager.GetFocusedElement(XamlRoot) is not DependencyObject focused || !IsWithin(focused, TableRegion)) return;

        var inField = focused is TextBox or CalendarDatePicker or CheckBox or ComboBox { IsDropDownOpen: false };
        if (!inField) return;
        e.Handled = true;
        var rowToSave = RowOf(focused);
        EntryList.Focus(FocusState.Programmatic);   // the box hands over what was typed
        // A number box finishes reading its text a moment after it loses focus, so save once that has happened.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _ = rowToSave is { IsEditing: true } ? ViewModel.CommitRowAsync(rowToSave) : ViewModel.CommitAllEditsAsync());
    }

    /// <summary>A press anywhere outside the table (and the month line panel) lets go of the row that is selected. A row being edited is not let go of.</summary>
    private void Page_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.SelectedEntry is null || ViewModel.IsRowEditing) return;   // a row being edited is only left with Enter, the green check or Esc
        var source = e.OriginalSource as DependencyObject;
        if (IsWithin(source, TableRegion) || IsWithin(source, DetailHost)) return;
        // A button above the table (multi-select, sort, hide) does its own work: taking the focus from it as it is pressed would swallow the click.
        for (var node = source; node is not null && !ReferenceEquals(node, this); node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (node is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase) return;
        ClearEditing();
    }

    private static bool IsWithin(DependencyObject? element, DependencyObject container)
    {
        for (var node = element; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (node == container) return true;
        return false;
    }

    /// <summary>The sort button: switches between newest first and oldest first.</summary>
    private void Sort_Click(object sender, RoutedEventArgs e) => ViewModel.SortIndex = ViewModel.SortIndex == 0 ? 1 : 0;

    // ---- Resizing columns and the side panel ----

    /// <summary>The shared widths of the table's columns and the edit panel.</summary>
    public ColumnLayout Layout => ColumnLayout.Current;

    private static bool s_layoutLoaded;
    private double _gripStart;

    private void Layout_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ColumnLayout.ShowItem) or nameof(ColumnLayout.ShowAccount) or nameof(ColumnLayout.Selecting)) FitTable();
        else UpdateTableWidth();
        if (e.PropertyName == nameof(ColumnLayout.ClearedTitle)) ClearedHeading.Title = Layout.ClearedTitle;
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

    // ---- The month a line stands for ----

    /// <summary>Puts the selected line's month in the boxes. A line has no date: only its month, and its place in the list.</summary>
    private void ShowMarkerMonth()
    {
        if (ViewModel.SelectedMarker is not { Month: { } month }) return;
        StartMonthBox.SelectedIndex = month.Month - 1;
        StartYearBox.Value = month.Year;
    }

    private void EditMarker_Click(object sender, RoutedEventArgs e)
    {
        ShowMarkerMonth();
        ViewModel.IsMarkerEditing = true;
    }

    private void CancelMarkerEdit_Click(object sender, RoutedEventArgs e) => ViewModel.IsMarkerEditing = false;

    /// <summary>Save: only now does the month chosen in the panel reach the line.</summary>
    private async void SaveMarker_Click(object sender, RoutedEventArgs e)
    {
        if (StartMonthBox.SelectedIndex >= 0 && !double.IsNaN(StartYearBox.Value))
            await ViewModel.SetSelectedMarkerMonthAsync(new DateOnly((int)StartYearBox.Value, StartMonthBox.SelectedIndex + 1, 1));
        ViewModel.IsMarkerEditing = false;
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
        var zoom = AppServices.TransactionsZoom;
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

    /// <summary>
    /// The page margin, and the panels that sit on the page's edges, follow the window's width; the table is fitted to the room it has.
    /// </summary>
    private void ApplyPageSize(double width)
    {
        var margin = Responsive.PageMargin(width);
        if (PageRoot.Margin != margin)
        {
            PageRoot.Margin = margin;
        }
    }

    /// <summary>Works out which columns show in the room the table has (see <see cref="ColumnLayout.Fit"/>), and scales the table only when even the least will not fit.</summary>
    private void FitTable()
    {
        Layout.Fit(TableScroll.ActualWidth);
        ClearedHeading.Title = Layout.ClearedTitle;
        var least = Layout.NeededLeast;
        if (Math.Abs(TableZoom.NaturalWidth - least) > 0.5) TableZoom.NaturalWidth = least;
        UpdateTableWidth();
    }

    /// <summary>A click on the memo icon (the memo is an icon in a narrow window): opens the memo in a small box to read or change.</summary>
    private void MemoIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TransactionRow row } anchor) return;
        if (!row.IsEditing) ViewModel.BeginEdit(row);

        var box = new TextBox { Text = row.Memo, PlaceholderText = "Memo", Width = 280 };
        var flyout = new Flyout { Content = box, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
        flyout.Opened += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        flyout.Closed += (_, _) => row.Memo = box.Text;
        box.KeyDown += (_, k) =>
        {
            if (k.Key != VirtualKey.Enter) return;
            k.Handled = true;
            row.Memo = box.Text;
            flyout.Hide();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _ = ViewModel.CommitRowAsync(row));
        };
        flyout.ShowAt(anchor);
    }


    private void TableScroll_SizeChanged(object sender, SizeChangedEventArgs e) => FitTable();

    private void UpdateTableWidth()
    {
        var width = Math.Max(TableScroll.ActualWidth, Layout.NeededNow);
        // Zoomed, the width is nudged (by less than a pixel) so the Memo column ends on a whole screen pixel, and so do the columns after it.
        var zoom = AppServices.TransactionsZoom;
        if (Math.Abs(zoom - 1) > 0.0001 && width > ColumnLayout.ActionsMinimum) width = Math.Round((width - ColumnLayout.ActionsMinimum) * zoom) / zoom + ColumnLayout.ActionsMinimum;
        TableRegion.Width = width;
        TableFrame.BorderThickness = new Thickness(Math.Abs(zoom - 1) < 0.0001 ? 1 : 1 / zoom);   // one screen pixel, so the table's edge is as crisp as at 100%
    }

    /// <summary>The navigation parameter is the account to show, or nothing for all accounts.</summary>
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        ViewModel.IsSelecting = false;   // selection mode never carries over from another table
        ViewModel.AccountId = e.Parameter is Guid id ? id : null;
        ViewModel.ShowBehind(e.Parameter as TransactionDrillDown);
    }

    /// <summary>In multi-select mode, a click anywhere in a row ticks or unticks it (the tick box does its own).</summary>
    private void Row_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!ViewModel.IsSelecting || sender is not FrameworkElement { DataContext: TransactionRow { IsNew: false } row }) return;
        for (var node = e.OriginalSource as DependencyObject; node is not null && !ReferenceEquals(node, sender); node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (node is CheckBox) return;
        row.IsTicked = !row.IsTicked;
        e.Handled = true;
    }

    /// <summary>Right-click (or the menu key) on a transaction row: Edit, Duplicate, Delete and Multi-select, or, in multi-select mode, only Delete and Exit multi-select.</summary>
    private void Row_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: TransactionRow row } target) return;
        args.Handled = true;
        if (row.IsNew) return;   // a row still being added has its own tick and bin

        var menu = new MenuFlyout();
        if (ViewModel.IsSelecting)
        {
            // In multi-select mode the menu only edits or deletes the ticked rows (or this row, when none is ticked yet) or leaves the mode.
            var chosen = ViewModel.TickedRows();
            if (chosen.Count == 0) chosen = [row];
            var editChosen = new MenuFlyoutItem { Text = chosen.Count > 1 ? $"Edit {chosen.Count} transactions" : "Edit", Icon = new FontIcon { Glyph = "\uE70F" } };
            editChosen.Click += (_, _) =>
            {
                // The ticked rows all open for editing together, and multi-select mode is left (each row has its own tick to save it).
                var rows = chosen.ToList();
                ViewModel.IsSelecting = false;
                ViewModel.BeginEditMany(rows);
            };
            menu.Items.Add(editChosen);
            var deleteChosen = new MenuFlyoutItem { Text = chosen.Count > 1 ? $"Delete {chosen.Count} transactions" : "Delete", Icon = new FontIcon { Glyph = "\uE74D" } };
            deleteChosen.Click += async (_, _) => await DeleteManyAsync(chosen);
            menu.Items.Add(deleteChosen);
            menu.Items.Add(new MenuFlyoutSeparator());
            var exit = new MenuFlyoutItem { Text = "Exit multi-select", Icon = new FontIcon { Glyph = "\uE711" } };
            exit.Click += (_, _) => ViewModel.IsSelecting = false;
            menu.Items.Add(exit);
        }
        else
        {
            if (row.IsEditing)
            {
                // A row being edited offers to save it (the same as the tick on the row), or to drop its changes. With several rows open, all of them can be saved or dropped together.
                var save = new MenuFlyoutItem { Text = "Save", Icon = new FontIcon { Glyph = "\uE73E" } };
                save.Click += (_, _) =>
                {
                    EntryList.Focus(FocusState.Programmatic);   // the box being typed in hands over what was typed
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _ = ViewModel.CommitRowAsync(row));
                };
                menu.Items.Add(save);

                var discard = new MenuFlyoutItem { Text = "Discard changes", Icon = new FontIcon { Glyph = "\uE711" } };
                discard.Click += (_, _) => { EntryList.Focus(FocusState.Programmatic); _ = ViewModel.CancelRowAsync(row); };
                menu.Items.Add(discard);

                if (ViewModel.EditingRows.Count > 1)
                {
                    menu.Items.Add(new MenuFlyoutSeparator());
                    var saveAll = new MenuFlyoutItem { Text = $"Save all {ViewModel.EditingRows.Count} rows being edited", Icon = new FontIcon { Glyph = "\uE74E" } };
                    saveAll.Click += (_, _) =>
                    {
                        EntryList.Focus(FocusState.Programmatic);
                        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _ = ViewModel.CommitAllEditsAsync());
                    };
                    menu.Items.Add(saveAll);
                    var discardAll = new MenuFlyoutItem { Text = "Discard all changes", Icon = new FontIcon { Glyph = "\uE74D" } };
                    discardAll.Click += (_, _) => { EntryList.Focus(FocusState.Programmatic); _ = ViewModel.CancelEditAsync(); };
                    menu.Items.Add(discardAll);
                }
            }
            else
            {
                var edit = new MenuFlyoutItem { Text = "Edit", Icon = new FontIcon { Glyph = "\uE70F" } };
                edit.Click += (_, _) => ViewModel.BeginEdit(row);
                menu.Items.Add(edit);

                var duplicate = new MenuFlyoutItem { Text = "Duplicate", Icon = new FontIcon { Glyph = "\uE8C8" } };
                duplicate.Click += async (_, _) =>
                {
                    await ViewModel.BeginDuplicateAsync(row);
                    if (ViewModel.Rows.OfType<TransactionRow>().FirstOrDefault(r => r.IsNew) is { } copy) await ShowNewRowAsync(copy, copy.IsTransfer);
                };
                menu.Items.Add(duplicate);
            }

            var delete = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "\uE74D" } };
            delete.Click += async (_, _) => await DeleteRowAsync(row);
            menu.Items.Add(delete);

            menu.Items.Add(new MenuFlyoutSeparator());
            var select = new MenuFlyoutItem { Text = "Multi-select", Icon = new FontIcon { Glyph = "\uE762" } };
            select.Click += (_, _) => { ViewModel.IsSelecting = true; row.IsTicked = true; };
            menu.Items.Add(select);
        }

        if (args.TryGetPosition(target, out var point)) menu.ShowAt(target, point);
        else menu.ShowAt(target);
    }

    /// <summary>A budget item typed into the drop-down that doesn't exist: ask for its category, create it, and select it.</summary>
    private async void Item_CreateRequested(object? sender, string name)
    {
        var groups = await AppServices.Store.GetGroupsAsync();
        if (groups.Count == 0)
        {
            ViewModel.Message = "Add a budget group first: use the + beside Cash Spend Planning on the budget screen.";
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
        if ((sender as FrameworkElement)?.DataContext is not TransactionRow row || !row.IsEditing) return;
        EntryList.Focus(FocusState.Programmatic);   // the box being typed in hands over what was typed
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _ = ViewModel.CommitRowAsync(row));
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var row = (sender as FrameworkElement)?.DataContext as TransactionRow ?? ViewModel.Selected;
        if (row is not null) await DeleteRowAsync(row);
    }

    // ---- The multi-select button of the toolbar ----

    private bool _selectShown;
    private Storyboard? _selectStory;

    /// <summary>The button above the table: it turns multi-select on, and when on it is the cross that turns it off.</summary>
    private void SelectToggle_Click(object sender, RoutedEventArgs e) => ViewModel.IsSelecting = !ViewModel.IsSelecting;

    /// <summary>The pencil: opens the ticked transactions for editing together, as Edit in the right-click menu does.</summary>
    private void EditMany_Click(object sender, RoutedEventArgs e)
    {
        var rows = ViewModel.TickedRows();
        if (rows.Count == 0) { ViewModel.Message = "Tick the transactions you want to edit first."; return; }
        ViewModel.IsSelecting = false;
        ViewModel.BeginEditMany(rows);
    }

    /// <summary>The bin: deletes the ticked transactions, after asking.</summary>
    private async void DeleteMany_Click(object sender, RoutedEventArgs e)
    {
        var rows = ViewModel.TickedRows();
        if (rows.Count == 0) { ViewModel.Message = "Tick the transactions you want to delete first."; return; }
        await DeleteManyAsync(rows);
    }

    /// <summary>
    /// Opens the multi-select button leftwards into the pencil, the bin and the cross (the multi-select icon turns into the cross), or folds
    /// the three back into the one button.
    /// </summary>
    private void ShowSelectButtons(bool selecting)
    {
        if (_selectShown == selecting) return;
        _selectShown = selecting;
        // While selecting, the sort, eye and more buttons are put away, so that the edit, delete and close buttons end at the table's right edge.
        SortButton.Visibility = EyeButton.Visibility = MoreButton.Visibility = selecting ? Visibility.Collapsed : Visibility.Visible;
        if (!selecting)
        {
            // Closing is instant: the three buttons are put away and the multi-select button is back as it was, with no folding.
            _selectStory?.Stop();
            EditManyButton.IsHitTestVisible = DeleteManyButton.IsHitTestVisible = false;
            SelectHost.Width = 48;
            EditSlide.X = 112;
            DeleteSlide.X = 56;
            EditManyButton.Opacity = DeleteManyButton.Opacity = 0;
            SelectIcon.Opacity = 1;
            CloseIcon.Opacity = 0;
            CloseSpin.Angle = -90;
            ToolTipService.SetToolTip(SelectToggleButton, "Multi-select: tick several transactions to edit or delete them together");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SelectToggleButton, "Multi-select");
            return;
        }

        _selectStory?.Stop();
        var story = _selectStory = new Storyboard();
        void Move(DependencyObject target, string property, double to, int beginMs, int durationMs, bool layout = false)
        {
            var animation = new DoubleAnimation
            {
                To = to, BeginTime = TimeSpan.FromMilliseconds(beginMs), Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, EnableDependentAnimation = layout,   // the width is a layout property
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            story.Children.Add(animation);
        }

        const double open = 160, shut = 48;
        if (selecting)
        {
            EditManyButton.IsHitTestVisible = DeleteManyButton.IsHitTestVisible = true;
            Move(SelectHost, "Width", open, 0, 300, layout: true);
            Move(DeleteSlide, "X", 0, 30, 270);
            Move(EditSlide, "X", 0, 0, 300);
            Move(DeleteManyButton, "Opacity", 1, 30, 200);
            Move(EditManyButton, "Opacity", 1, 0, 200);
            Move(SelectIcon, "Opacity", 0, 0, 120);
            Move(CloseIcon, "Opacity", 1, 60, 160);
            Move(CloseSpin, "Angle", 0, 0, 280);
        }
        else
        {
            EditManyButton.IsHitTestVisible = DeleteManyButton.IsHitTestVisible = false;
            Move(EditSlide, "X", 112, 0, 260);
            Move(DeleteSlide, "X", 56, 0, 260);
            Move(EditManyButton, "Opacity", 0, 0, 160);
            Move(DeleteManyButton, "Opacity", 0, 0, 160);
            Move(SelectHost, "Width", shut, 60, 260, layout: true);
            Move(CloseIcon, "Opacity", 0, 0, 120);
            Move(SelectIcon, "Opacity", 1, 120, 160);
            Move(CloseSpin, "Angle", -90, 0, 260);
        }
        ToolTipService.SetToolTip(SelectToggleButton, selecting ? "Close multi-select" : "Multi-select: tick several transactions to edit or delete them together");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SelectToggleButton, selecting ? "Close multi-select" : "Multi-select");

        story.Completed += (_, _) =>
        {
            // Keep where it ended up as the real values, then let the animation go.
            SelectHost.Width = selecting ? open : shut;
            EditSlide.X = selecting ? 0 : 112;
            DeleteSlide.X = selecting ? 0 : 56;
            EditManyButton.Opacity = DeleteManyButton.Opacity = selecting ? 1 : 0;
            SelectIcon.Opacity = selecting ? 0 : 1;
            CloseIcon.Opacity = selecting ? 1 : 0;
            CloseSpin.Angle = selecting ? 0 : -90;
            story.Stop();
        };
        story.Begin();
    }

    // ---- Ticked transactions ----

    private void TickAll_Click(object sender, RoutedEventArgs e) => ViewModel.SetAllTicked(TickAllBox.IsChecked == true);

    /// <summary>Keeps the box in the heading in step with the ticks: ticked when every transaction is.</summary>
    private void UpdateTicks(object? sender, EventArgs e)
    {
        var count = ViewModel.TickedRows().Count;
        TickAllBox.IsChecked = count > 0 && count == ViewModel.TransactionCount;
        // The edit and delete buttons of the toolbar work on the ticked rows, so they wait until some are ticked.
        EditManyButton.IsEnabled = DeleteManyButton.IsEnabled = count > 0;
        EditIcon.Opacity = DeleteIcon.Opacity = count > 0 ? 1 : 0.4;
    }


    private async Task DeleteManyAsync(IReadOnlyList<TransactionRow> rows)
    {
        if (rows.Count == 1) { await DeleteRowAsync(rows[0]); return; }
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete these {rows.Count} transactions?",
            Content = rows.Any(r => r.IsTransfer) ? "A transfer is deleted on both sides. Ctrl+Z brings them all back." : "Ctrl+Z brings them all back.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.DeleteManyAsync(rows);
    }

    // ---- Importing a file, and exporting the table to Excel ----

    private bool ImportWithAccount => ViewModel.AccountId is null;

    /// <summary>
    /// Import: Excel, CSV or text, or a zip of those. A workbook exported from Speed Budget (it has its own headings, budget items and
    /// month lines) is added as it is; any other file goes through the wizard, which asks how the columns map and then reconciles the
    /// lines with the table: lines already there are cleared, lines not there are added.
    /// </summary>
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await ExcelFiles.PickImportAsync();
            if (path is null) return;

            if (Path.GetExtension(path).ToLowerInvariant() is ".xlsx" or ".xlsm")
            {
                TransactionImportFile? own = null;
                try { own = await Task.Run(() => TransactionImport.Read(path, ImportWithAccount)); } catch (Exception) { /* not a workbook we can read this way: the wizard tries it */ }
                if (own is { Rows.Count: > 0 }) { await ImportOwnFileAsync(own); return; }
            }

            var tables = await Task.Run(() => BankFile.ReadTables(path));
            if (tables.Count == 0) { await ExcelFiles.ShowAsync(XamlRoot, "Nothing to import", "No table of transactions was found in the file. Pick an Excel workbook, a CSV or text file, or a zip that holds them."); return; }
            if (ViewModel.ImportAccounts().Count == 0) { await ExcelFiles.ShowAsync(XamlRoot, "Nothing to import into", "There is no account kept by transactions to import into."); return; }
            var wizard = new ImportWizardDialog(ViewModel, tables, Path.GetFileName(path)) { XamlRoot = XamlRoot };
            await wizard.ShowAsync();
            if (wizard.Outcome is { } check)
            {
                // The file's balance against the account's. When they differ, the user can go on to find out why and fix it.
                var shown = await new BalanceCheckDialog(check) { XamlRoot = XamlRoot }.ShowAsync();
                if (shown == ContentDialogResult.Primary)
                {
                    var fix = new BalanceFixDialog(ViewModel, check) { XamlRoot = XamlRoot };
                    await fix.ShowAsync();
                    if (fix.Resolved)   // the cleared balance agrees now: check everything again and show the result
                    {
                        var again = await ViewModel.CompareStatementAsync(check.AccountId, check.Info);
                        await new BalanceCheckDialog(check with { Comparisons = again }) { XamlRoot = XamlRoot }.ShowAsync();
                    }
                }
            }
        }
        catch (Exception ex) { await ExcelFiles.ShowAsync(XamlRoot, "The file could not be imported", ex.Message); }
    }

    private async Task ImportOwnFileAsync(TransactionImportFile read)
    {
        var preview = await ViewModel.PreviewImportAsync(read.Rows);
        var problems = read.Problems.Concat(preview.Skipped).ToList();
        var text = $"{preview.Added} transaction{(preview.Added == 1 ? "" : "s")} will be added";
        text += preview.MonthLines > 0 ? $", with {preview.MonthLines} new month line{(preview.MonthLines == 1 ? "" : "s")}." : ".";
        foreach (var note in preview.Notes) text += $"\n\n{note}";
        if (problems.Count > 0) text += $"\n\n{problems.Count} line{(problems.Count == 1 ? "" : "s")} will be skipped:\n{ExcelFiles.ProblemText(problems)}";
        if (preview.Added == 0 && preview.MonthLines == 0) { await ExcelFiles.ShowAsync(XamlRoot, "Nothing to import", text); return; }
        if (!await ExcelFiles.ConfirmAsync(XamlRoot, "Import these transactions?", text)) return;

        var result = await ViewModel.ImportAsync(read.Rows);
        if (result is not null) ViewModel.Message = "";
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await ExcelFiles.PickSaveAsync(ViewModel.PageTitle.Replace(' ', '-').ToLowerInvariant() + "-transactions");
            if (path is null) return;
            await ViewModel.ExportAsync(path);
            await ExcelFiles.ShowAsync(XamlRoot, "Exported", $"The transactions were saved to {path}.");
        }
        catch (Exception ex) { await ExcelFiles.ShowAsync(XamlRoot, "The transactions could not be exported", ex.Message); }
    }

    private async Task DeleteRowAsync(TransactionRow row)
    {
        if (row.IsNew) { _ = ViewModel.CancelRowAsync(row); return; }   // nothing saved yet: just drop it
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

    /// <summary>The x in the date cell: the transaction is left with no date, which makes it an expected transaction.</summary>
    private void ClearDate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TransactionRow row) row.DateOffset = null;
    }

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
        await ShowNewRowAsync(row, transfer);
    }

    /// <summary>
    /// Brings a row that has just been opened for typing into view and puts the cursor in its first box (the counterparty, or the other
    /// account of a transfer). A row far down a long list is not laid out at once, so it goes again until the row is really there.
    /// </summary>
    private async Task ShowNewRowAsync(TransactionRow row, bool transfer)
    {
        try
        {
            for (var pass = 0; pass < 8; pass++)
            {
                EntryList.ScrollIntoView(row);
                await Task.Delay(pass == 0 ? 120 : 50);   // the row's boxes exist once the list has laid it out
                if (EntryList.ContainerFromItem(row) is DependencyObject container)
                {
                    if (transfer) { if (FindFirst<ComboBox>(container) is { } other) other.Focus(FocusState.Programmatic); }
                    else if (FindFirst<PayeePicker>(container) is { } picker) picker.FocusPicker();
                    return;
                }
            }
        }
        catch (Exception ex) { Diagnostics.Note($"could not show the new row: {ex.Message}"); }
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
            await ViewModel.AddMonthAsync(dialog.Month);
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
