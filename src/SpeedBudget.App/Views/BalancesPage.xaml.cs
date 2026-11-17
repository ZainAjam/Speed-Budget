using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SpeedBudget.App.ViewModels;
using SpeedBudget.Data;
using Windows.Storage.Pickers;
using Windows.System;

namespace SpeedBudget.App.Views;

/// <summary>An account kept by its balances (see <see cref="BalancesViewModel"/>), shown in place of the transactions table.</summary>
public sealed partial class BalancesPage : Page, IReloadable
{
    public BalancesViewModel ViewModel { get; } = new();

    public BalancesPage()
    {
        InitializeComponent();
        // A press outside the table lets go of the highlighted line, as on the transactions tables (handled presses count too).
        AddHandler(PointerPressedEvent, new PointerEventHandler(Page_PointerPressed), true);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        BalanceLine.TickChanged -= Line_TickChanged;
        Columns.PropertyChanged -= Columns_Changed;
        ViewModel.PropertyChanged -= Columns_Changed;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        BalanceLine.TickChanged -= Line_TickChanged;
        BalanceLine.TickChanged += Line_TickChanged;
        Columns.PropertyChanged -= Columns_Changed;
        Columns.PropertyChanged += Columns_Changed;
        ViewModel.PropertyChanged -= Columns_Changed;
        ViewModel.PropertyChanged += Columns_Changed;
        SetSelecting(false);   // selection mode never carries over from another table
        try
        {
            await LoadColumnsAsync();
            if (e.Parameter is Guid accountId) await ViewModel.LoadAsync(accountId);
        }
        catch (Exception ex) { Diagnostics.Note($"balances page could not load: {ex.Message}"); }   // switching accounts quickly may leave this page behind
        UpdateTableFit();
    }

    /// <summary>When the window is too narrow for the table at full size, the table is made smaller to fit instead of being cut off.</summary>
    private void Columns_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateTableFit();

    private void UpdateTableFit()
    {
        var width = ViewModel.ShowMonths
            ? Columns.Get(7) + Columns.Get(8) + Columns.Get(9) + Columns.Get(10)
            : Columns.LeadWidth.Value + Columns.Get(0) + Columns.Get(1) + Columns.Get(2) + Columns.Get(3) + Columns.Get(4) + Columns.Get(5) + Columns.CommentMin + 170;
        if (Math.Abs(TableZoom.NaturalWidth - width) > 0.5) TableZoom.NaturalWidth = width;
    }

    /// <summary>The totals beside the title; when they do not fit there, they move under it.</summary>
    private void Header_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        TitleText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        TotalsPanel.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        // The heading is as wide as the room, or as it needs if that is more; the Viewbox then shrinks it to fit.
        var natural = Math.Ceiling(TitleText.DesiredSize.Width + HeaderGrid.ColumnSpacing + TotalsPanel.DesiredSize.Width);
        var width = Math.Max(natural, HeaderHost.ActualWidth);
        if (width > 0 && !double.IsNaN(width) && !(Math.Abs(HeaderGrid.Width - width) < 0.5)) HeaderGrid.Width = width;
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

    /// <summary>Add Entry: a new line opens at the top of the list, ready to be filled in.</summary>
    private void AddEntry_Click(object sender, RoutedEventArgs e) => ViewModel.BeginAdd();

    /// <summary>The balance box of a line that has just been added takes the cursor, so the amount can be typed straight away.</summary>
    private void BalanceBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is NumberBox { DataContext: BalanceLine { IsNew: true } } box)
            box.DispatcherQueue.TryEnqueue(() => box.Focus(FocusState.Programmatic));
    }

    // ---- Dragging the edge of a column heading to change the column's width ----

    public BalanceColumns Columns => BalanceColumns.Shared;

    private static bool s_columnsLoaded;
    private double[] _gripStart = [];

    private async Task LoadColumnsAsync()
    {
        if (s_columnsLoaded) return;
        s_columnsLoaded = true;
        try { Columns.Load(await AppServices.Store.GetLocalSettingAsync("balanceColumns")); }
        catch { /* the normal sizes are fine */ }
    }

    private static int ColumnOf(object? grip) => int.Parse((string)((FrameworkElement)grip!).Tag, System.Globalization.CultureInfo.InvariantCulture);

    private static (int First, int Last) GroupOf(int column) =>
        column <= BalanceColumns.DailyLast ? (BalanceColumns.DailyFirst, BalanceColumns.DailyLast) : (BalanceColumns.MonthFirst, BalanceColumns.MonthLast);

    private void Grip_Started(object? sender, EventArgs e)
    {
        // The Comment column may be wider than its own width because it takes the room that is left; a drag starts from what is on screen.
        var onScreen = Math.Floor(DailyHeading.ActualWidth - 46 - 170);
        for (var i = BalanceColumns.DailyFirst; i < BalanceColumns.CommentIndex; i++) onScreen -= Columns.Get(i);
        if (onScreen > Columns.Get(BalanceColumns.CommentIndex)) Columns.Set(BalanceColumns.CommentIndex, onScreen);
        _gripStart = Columns.Snapshot();
    }

    private void Grip_Moved(object? sender, double dx)
    {
        var column = ColumnOf(sender);
        var (first, last) = GroupOf(column);
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        try { Columns.Resize(first, last, column, dx / AppServices.TransactionsZoom, shift, _gripStart); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Resizing a column failed: {ex.Message}"); }
    }

    private async void Grip_Completed(object? sender, EventArgs e)
    {
        try { await AppServices.Store.SetLocalSettingAsync("balanceColumns", Columns.Serialize()); }
        catch { /* not remembering the sizes must never get in the way */ }
    }

    // ---- Ticking lines, to delete them in one go ----

    /// <summary>The ticked lines (a new line that is not saved yet is never counted).</summary>
    private List<BalanceLine> Ticked() => ViewModel.Lines.Where(l => l.IsTicked && !l.IsNew).ToList();

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var tick = SelectAllBox.IsChecked == true;
        _tickingAll = true;
        try { foreach (var line in ViewModel.Lines) if (!line.IsNew) line.IsTicked = tick; }
        finally { _tickingAll = false; }
        UpdateSelection();
    }

    private bool _tickingAll;

    private void Line_TickChanged(object? sender, EventArgs e)
    {
        if (!_tickingAll) UpdateSelection();
    }

    /// <summary>Keeps the box in the heading in step with the ticks: ticked when every line is.</summary>
    private void UpdateSelection()
    {
        var count = Ticked().Count;
        SelectAllBox.IsChecked = count > 0 && count == ViewModel.Lines.Count(l => !l.IsNew);
    }

    /// <summary>Multi-select mode on or off: the tick boxes come and go with it, and leaving it clears the ticks.</summary>
    private void SetSelecting(bool selecting)
    {
        if (!selecting) foreach (var line in ViewModel.Lines) line.IsTicked = false;
        ViewModel.IsSelecting = selecting;
        UpdateSelection();
    }

    /// <summary>In multi-select mode, a click anywhere in a line ticks or unticks it (the tick box does its own).</summary>
    private void Line_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!ViewModel.IsSelecting || sender is not FrameworkElement { Tag: BalanceLine { IsNew: false } line }) return;
        for (var node = e.OriginalSource as DependencyObject; node is not null && !ReferenceEquals(node, sender); node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (node is CheckBox) return;
        line.IsTicked = !line.IsTicked;
        e.Handled = true;
    }

    private void Page_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (LinesList.SelectedItem is null || ViewModel.Lines.Any(l => l.IsEditing)) return;
        for (var node = e.OriginalSource as DependencyObject; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, DailyTable)) return;
        LinesList.SelectedItem = null;
    }

    /// <summary>Esc leaves multi-select mode.</summary>
    private void Escape_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ViewModel.IsSelecting && LinesList.SelectedItem is not null && !ViewModel.Lines.Any(l => l.IsEditing)) { LinesList.SelectedItem = null; args.Handled = true; return; }   // Esc lets go of the highlighted line
        if (!ViewModel.IsSelecting || ViewModel.Lines.Any(l => l.IsEditing)) return;
        args.Handled = true;
        SetSelecting(false);
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var lines = Ticked();
        if (lines.Count == 0) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = lines.Count == 1 ? "Delete this balance?" : $"Delete these {lines.Count} balances?",
            Content = "They are removed, and the changes, gains and losses are worked out again without them. Ctrl+Z brings them back.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.DeleteManyAsync(lines);
    }

    /// <summary>Right-click on a line: Edit, Delete and Multi-select, or, in multi-select mode, only Delete and Exit multi-select.</summary>
    private void Line_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: BalanceLine line } target) return;
        args.Handled = true;
        if (line.IsNew) return;   // a line still being added has its own tick and cross

        var menu = new MenuFlyout();
        if (ViewModel.IsSelecting)
        {
            // In multi-select mode the menu only deletes the ticked lines (or this one, when none is ticked yet) or leaves the mode.
            var chosen = Ticked();
            if (chosen.Count == 0) chosen = [line];
            var deleteChosen = new MenuFlyoutItem { Text = chosen.Count > 1 ? $"Delete {chosen.Count} balances" : "Delete", Icon = new FontIcon { Glyph = "\uE74D" } };
            deleteChosen.Click += (_, _) =>
            {
                if (chosen.Count > 1) DeleteSelected_Click(this, new RoutedEventArgs());
                else Delete_Click(new Button { Tag = chosen[0] }, new RoutedEventArgs());
            };
            menu.Items.Add(deleteChosen);
            menu.Items.Add(new MenuFlyoutSeparator());
            var exit = new MenuFlyoutItem { Text = "Exit multi-select", Icon = new FontIcon { Glyph = "\uE711" } };
            exit.Click += (_, _) => SetSelecting(false);
            menu.Items.Add(exit);
        }
        else
        {
            if (!line.IsEditing)
            {
                var edit = new MenuFlyoutItem { Text = "Edit", Icon = new FontIcon { Glyph = "\uE70F" } };
                edit.Click += (_, _) => ViewModel.BeginChange(line);
                menu.Items.Add(edit);
            }
            var delete = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "\uE74D" } };
            delete.Click += (_, _) => Delete_Click(new Button { Tag = line }, new RoutedEventArgs());
            menu.Items.Add(delete);

            menu.Items.Add(new MenuFlyoutSeparator());
            var select = new MenuFlyoutItem { Text = "Multi-select", Icon = new FontIcon { Glyph = "\uE762" } };
            select.Click += (_, _) => { SetSelecting(true); line.IsTicked = true; };
            menu.Items.Add(select);
        }
        if (args.TryGetPosition(target, out var point)) menu.ShowAt(target, point);
        else menu.ShowAt(target);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await ExcelFiles.PickSaveAsync(ViewModel.Title.Replace(' ', '-').ToLowerInvariant() + "-balances");
            if (path is null) return;
            await ViewModel.ExportAsync(path);
            ViewModel.Tell($"Exported the balances to {path}.");
        }
        catch (Exception ex) { await ShowAsync("The balances could not be exported", ex.Message); }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BalanceLine line }) ViewModel.BeginChange(line);
    }

    private async void SaveLine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BalanceLine line }) await ViewModel.SaveChangeAsync(line);
    }

    private void CancelLine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BalanceLine line }) ViewModel.CancelChange(line);
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

    /// <summary>
    /// An amount box only takes what was typed in it when it loses the focus, but Enter saves while the cursor is still in it. So what is
    /// typed in the box with the cursor is read and put in the box first.
    /// </summary>
    private void CommitBox()
    {
        DependencyObject? node = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (node is not null and not NumberBox) node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
        if (node is not NumberBox box || Money.FindTextBox(box) is not { } input || box.NumberFormatter is not Windows.Globalization.NumberFormatting.INumberParser parser) return;
        box.Value = string.IsNullOrWhiteSpace(input.Text) ? double.NaN : parser.ParseDouble(input.Text) ?? double.NaN;
    }

    /// <summary>In a line being changed: Enter saves it and Esc leaves it as it was.</summary>
    private void Line_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BalanceLine { IsEditing: true } line }) return;
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            CommitBox();
            DispatcherQueue.TryEnqueue(async () => await ViewModel.SaveChangeAsync(line));
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            ViewModel.CancelChange(line);
        }
    }

    // ---- Import from Excel ----

    private static void StartWithWindow(object picker) =>
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

    /// <summary>Reads an Excel file, shows what it would do, and brings the lines in when the user agrees.</summary>
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".xlsx");
            StartWithWindow(picker);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var read = await Task.Run(() => BalanceImport.Read(file.Path));
            if (read.Rows.Count == 0)
            {
                var why = read.Problems.Count == 0 ? "There are no lines with a date and a balance in the file. The first row needs the headings Date, Balance, Withdrawal and Comment (an export from Speed Budget has them)." : ProblemText(read.Problems);
                await ShowAsync("Nothing to import", why);
                return;
            }

            var preview = await ViewModel.PreviewImportAsync(read.Rows);
            var text = $"{read.Rows.Count} line{(read.Rows.Count == 1 ? "" : "s")} found in the file: {preview.Added} for new dates and {preview.Updated} for dates already here, which take the figures in the file.";
            if (read.Problems.Count > 0) text += $"\n\n{read.Problems.Count} line{(read.Problems.Count == 1 ? "" : "s")} will be skipped:\n{ProblemText(read.Problems)}";
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Import these balances?",
                Content = new ScrollViewer { MaxHeight = 360, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } },
                PrimaryButtonText = "Import",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.ImportAsync(read.Rows);
        }
        catch (Exception ex) { await ShowAsync("The file could not be imported", ex.Message); }
    }

    private static string ProblemText(IReadOnlyList<ImportProblem> problems)
    {
        var lines = problems.Take(8).Select(p => $"Row {p.Line}: {p.Message}").ToList();
        if (problems.Count > 8) lines.Add($"…and {problems.Count - 8} more.");
        return string.Join("\n", lines);
    }

    private async Task ShowAsync(string title, string message) =>
        await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, CloseButtonText = "OK" }.ShowAsync();
}
