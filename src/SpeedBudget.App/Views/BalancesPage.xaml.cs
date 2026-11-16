using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
