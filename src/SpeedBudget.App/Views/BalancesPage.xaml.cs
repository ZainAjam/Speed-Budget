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
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        SetSelecting(false);   // selection mode never carries over from another table
        await LoadColumnsAsync();
        if (e.Parameter is Guid accountId) await ViewModel.LoadAsync(accountId);
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

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (SelectAllBox.IsChecked == true) LinesList.SelectAll();
        else LinesList.SelectedItems.Clear();
    }

    private void Lines_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelection();

    /// <summary>In selection mode the Delete selected and Done buttons show; Delete selected works once something is ticked.</summary>
    private void UpdateSelection()
    {
        var count = LinesList.SelectedItems.Count;
        var selecting = ViewModel.IsSelecting;
        DeleteSelectedButton.Visibility = selecting ? Visibility.Visible : Visibility.Collapsed;
        DeleteSelectedButton.IsEnabled = count > 0;
        SelectDoneButton.Visibility = DeleteSelectedButton.Visibility;
        DeleteSelectedText.Text = count > 0 ? $"Delete selected ({count})" : "Delete selected";
        SelectAllBox.IsChecked = count > 0 && count == ViewModel.Lines.Count;
    }

    /// <summary>Selection mode on or off: the tick boxes come and go with it, and leaving it clears the ticks.</summary>
    private void SetSelecting(bool selecting)
    {
        if (!selecting && LinesList.SelectionMode != ListViewSelectionMode.None) LinesList.SelectedItems.Clear();
        LinesList.SelectionMode = selecting ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
        ViewModel.IsSelecting = selecting;
        UpdateSelection();
    }

    private void SelectDone_Click(object sender, RoutedEventArgs e) => SetSelecting(false);

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var lines = LinesList.SelectedItems.OfType<BalanceLine>().ToList();
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

    /// <summary>Right-click on a line: with several ticked, only Delete; with one, Edit and Delete.</summary>
    private void Line_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: BalanceLine line } target) return;
        args.Handled = true;
        if (line.IsNew) return;   // a line still being added has its own tick and cross

        var menu = new MenuFlyout();
        var chosen = LinesList.SelectedItems.OfType<BalanceLine>().Where(l => !l.IsNew).ToList();
        if (chosen.Count > 1 && chosen.Contains(line))
        {
            var deleteAll = new MenuFlyoutItem { Text = $"Delete {chosen.Count} balances", Icon = new FontIcon { Glyph = "" } };
            deleteAll.Click += (_, _) => DeleteSelected_Click(this, new RoutedEventArgs());
            menu.Items.Add(deleteAll);
        }
        else
        {
            if (!line.IsEditing)
            {
                var edit = new MenuFlyoutItem { Text = "Edit", Icon = new FontIcon { Glyph = "" } };
                edit.Click += (_, _) => ViewModel.BeginChange(line);
                menu.Items.Add(edit);
            if (!ViewModel.IsSelecting)
            {
                var select = new MenuFlyoutItem { Text = "Select", Icon = new FontIcon { Glyph = "\uE762" } };
                select.Click += (_, _) => { SetSelecting(true); LinesList.SelectedItems.Add(line); };
                menu.Items.Add(select);
            }
            }
            var delete = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "" } };
            delete.Click += (_, _) => Delete_Click(new Button { Tag = line }, new RoutedEventArgs());
            menu.Items.Add(delete);
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

    /// <summary>Saves an empty Excel file with the right columns, for the user to fill in.</summary>
    private async void GetTemplate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = "balances-template" };
            picker.FileTypeChoices.Add("Excel workbook", new List<string> { ".xlsx" });
            StartWithWindow(picker);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            BalanceImport.CreateTemplate(file.Path);
            ViewModel.Tell($"Saved the template to {file.Path}. Fill it in, then choose Excel import, Import a filled-in template.");
        }
        catch (Exception ex) { await ShowAsync("The template could not be saved", ex.Message); }
    }

    /// <summary>Reads a filled-in template, shows what it would do, and brings the lines in when the user agrees.</summary>
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
                var why = read.Problems.Count == 0 ? "There are no lines with a date and a balance in the file." : ProblemText(read.Problems);
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
