using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

public sealed partial class BudgetPage : Page
{
    // Widths in the XAML: the name column, and each month's column.
    private const double NameColumnWidth = 220;
    private const double MonthColumnWidth = 330;
    private const double PageMargins = 64 + 20; // left and right margin, and the vertical scroll bar

    public BudgetViewModel ViewModel { get; } = new();

    public BudgetPage()
    {
        InitializeComponent();
        SummaryLabelList.ItemsSource = BudgetViewModel.SummaryLabels;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var firstRun = await AppServices.Store.IsEmptyAsync() && !FirstRunDismissed.Value;
        Welcome.Visibility = firstRun ? Visibility.Visible : Visibility.Collapsed;
        BudgetContent.Visibility = firstRun ? Visibility.Collapsed : Visibility.Visible;
        if (firstRun) return;

        FitMonths(ActualWidth);
        await ViewModel.LoadAsync();
        EmptyHint.Visibility = ViewModel.HasItems ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Shows as many months side by side as the width allows, and at least one.</summary>
    private void FitMonths(double width) =>
        ViewModel.SetCapacity((int)Math.Floor((width - PageMargins - NameColumnWidth) / MonthColumnWidth));

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => FitMonths(e.NewSize.Width);

    private async void Starter_Click(object sender, RoutedEventArgs e)
    {
        await AppServices.Store.SeedStarterItemsAsync();
        await RefreshAsync();
    }

    private async void Empty_Click(object sender, RoutedEventArgs e)
    {
        FirstRunDismissed.Value = true;
        await RefreshAsync();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ViewModel.GoBack();

    private void Forward_Click(object sender, RoutedEventArgs e) => ViewModel.GoForward();

    private void Chevron_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is BudgetLine { Group: { } group }) group.IsExpanded = !group.IsExpanded;
    }

    /// <summary>The month's "…" button: fill this month's budget from the one before it.</summary>
    private void MonthMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MonthColumn column } button) return;
        var menu = new MenuFlyout();
        var copy = new MenuFlyoutItem { Text = "Copy last month's budget (items not yet set)" };
        copy.Click += async (_, _) => await ViewModel.CopyLastMonthAsync(column);
        var spent = new MenuFlyoutItem { Text = "Budget what was spent last month" };
        spent.Click += async (_, _) => await ViewModel.BudgetLastMonthsSpendingAsync(column);
        menu.Items.Add(copy);
        menu.Items.Add(spent);
        menu.ShowAt(button);
    }

    /// <summary>Right-click (or the menu key) on an "Available now" figure: hold some of it for next month.</summary>
    private void Income_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: IncomeCell cell } target) return;
        var menu = new MenuFlyout();

        var hold = new MenuFlyoutItem { Text = "Hold for next month…", IsEnabled = cell.CanHold };
        hold.Click += async (_, _) =>
        {
            var dialog = new HoldDialog(cell) { XamlRoot = XamlRoot };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.HoldAsync(cell, dialog.AmountCents);
        };
        menu.Items.Add(hold);

        if (cell.CanRelease)
        {
            var release = new MenuFlyoutItem { Text = "Release the held amount" };
            release.Click += async (_, _) => await ViewModel.ReleaseAsync(cell);
            menu.Items.Add(release);
        }

        if (args.TryGetPosition(target, out var point)) menu.ShowAt(target, point);
        else menu.ShowAt(target);
        args.Handled = true;
    }
}

/// <summary>Remembers, for this run, that the user chose to start empty.</summary>
internal static class FirstRunDismissed
{
    public static bool Value { get; set; }
}
