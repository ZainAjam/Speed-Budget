using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

public sealed partial class BudgetPage : Page
{
    // Widths in the XAML: the least the name column is given, and each month's column.
    private const double NameColumnWidth = 240;
    private const double MonthColumnWidth = 330;
    private const double PageMargins = 64 + 20; // left and right margin, and the vertical scroll bar

    public BudgetViewModel ViewModel { get; } = new();

    private Guid? _revealItem;

    /// <summary>The navigation parameter, when there is one, is a budget item to bring into view (from a pinned-item chip).</summary>
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e) =>
        _revealItem = e.Parameter is Guid id ? id : null;

    public BudgetPage()
    {
        InitializeComponent();
        SummaryLabelList.ItemsSource = BudgetViewModel.SummaryLabels;
        Loaded += async (_, _) => await RefreshAsync();

        // Pressing anywhere that is not another control ends the edit in the budget cell being typed in. Handled
        // events are included, since the scroll viewer and the text would otherwise swallow the press.
        AddHandler(PointerPressedEvent, new PointerEventHandler(Page_PointerPressed), handledEventsToo: true);
    }

    private async Task RefreshAsync()
    {
        var firstRun = await AppServices.Store.IsEmptyAsync() && !FirstRunDismissed.Value;
        Welcome.Visibility = firstRun ? Visibility.Visible : Visibility.Collapsed;
        BudgetContent.Visibility = firstRun ? Visibility.Collapsed : Visibility.Visible;
        if (firstRun)
        {
            // The guided setup opens by itself the first time; closing it leaves the welcome buttons to reopen it.
            if (!SetupOffered.Value)
            {
                SetupOffered.Value = true;
                await RunSetupAsync();
            }
            return;
        }

        FitMonths(ActualWidth);
        await ViewModel.LoadAsync();
        EmptyHint.Visibility = ViewModel.HasItems ? Visibility.Collapsed : Visibility.Visible;

        // Arrived from a pinned-item chip: scroll to that item and light it up. The page opens on the current month.
        if (_revealItem is { } itemId)
        {
            _revealItem = null;
            if (ViewModel.Reveal(itemId) is { } line)
            {
                await Task.Delay(250); // let the rows lay out first
                (OutflowNames.ContainerFromItem(line) as UIElement)?.StartBringIntoView(
                    new BringIntoViewOptions { AnimationDesired = true, VerticalAlignmentRatio = 0.4 });
            }
        }
    }

    /// <summary>Shows as many months side by side as the width allows, and at least one.</summary>
    private void FitMonths(double width) =>
        ViewModel.SetCapacity((int)Math.Floor((width - PageMargins - NameColumnWidth) / MonthColumnWidth));

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => FitMonths(e.NewSize.Width);

    private async void Setup_Click(object sender, RoutedEventArgs e) => await RunSetupAsync();

    private bool _setupOpen;

    private async Task RunSetupAsync()
    {
        if (_setupOpen) return;   // one dialog at a time
        _setupOpen = true;
        try
        {
            var wizard = new SetupWizardDialog(askStartMonth: await AppServices.Store.IsEmptyAsync()) { XamlRoot = XamlRoot };
            await wizard.ShowAsync();
            if (wizard.Result is not null) await RefreshAsync();
        }
        finally { _setupOpen = false; }
    }

    private async void Empty_Click(object sender, RoutedEventArgs e)
    {
        FirstRunDismissed.Value = true;
        await RefreshAsync();
    }

    private async void Back_Click(object sender, RoutedEventArgs e) => await ViewModel.GoBackAsync();

    private void Forward_Click(object sender, RoutedEventArgs e) => ViewModel.GoForward();

    // The two view buttons act like a pair of radio buttons: choosing one sets the view, and the chosen one can't be
    // switched off by pressing it again. They react to the checked state, so keyboard use works the same as a click.
    private async void SingleMonth_Checked(object sender, RoutedEventArgs e) => await ViewModel.SetSingleMonthAsync(true);

    private async void ManyMonths_Checked(object sender, RoutedEventArgs e) => await ViewModel.SetSingleMonthAsync(false);

    private void SingleMonth_Unchecked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ShowSingleMonth) SingleButton.IsChecked = true;
    }

    private void ManyMonths_Unchecked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ShowManyMonths) ManyButton.IsChecked = true;
    }

    // ---- Editing budgeted amounts from the keyboard ----

    /// <summary>On entering a cell: remember its value for Esc, and select it so typing replaces it.</summary>
    private void Budgeted_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not NumberBox box) return;
        box.Tag = box.Value;
        Money.FindTextBox(box)?.SelectAll();
    }

    /// <summary>
    /// Enter saves and moves down to the next cell; Esc cancels the edit; Up and Down move between cells. The press is
    /// seen before the box's own handling (which would otherwise step the number up or down on the arrow keys).
    /// Leaving a cell is what saves it, so every route out of the cell commits what was typed.
    /// </summary>
    private void Budgeted_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not NumberBox box) return;
        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                if (NeighbourBox(box, down: true) is { } below) below.Focus(FocusState.Keyboard);
                else BudgetScroll.Focus(FocusState.Programmatic);
                break;
            case VirtualKey.Down:
            case VirtualKey.Up:
                e.Handled = true;
                if (NeighbourBox(box, down: e.Key == VirtualKey.Down) is { } next) next.Focus(FocusState.Keyboard);
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                // Put the saved amount back into the field being typed in. (Setting the box's own Text does nothing when it
                // already equals the saved text, which leaves the typed digits in place to be saved on leaving.)
                if (box.Tag is double original && box.NumberFormatter is { } formatter && Money.FindTextBox(box) is { } input)
                    input.Text = formatter.FormatDouble(original);
                BudgetScroll.Focus(FocusState.Programmatic);
                break;
        }
    }

    /// <summary>The visible budget cell above or below this one in the same month, skipping hidden rows and headings.</summary>
    private static NumberBox? NeighbourBox(NumberBox from, bool down)
    {
        DependencyObject? node = from;
        while (node is not null and not ItemsControl) node = VisualTreeHelper.GetParent(node);
        if (node is null) return null;

        var boxes = new List<NumberBox>();
        void Collect(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is NumberBox box && box.ActualHeight > 0) boxes.Add(box);
                else Collect(child);
            }
        }
        Collect(node);

        var index = boxes.IndexOf(from) + (down ? 1 : -1);
        return index >= 0 && index < boxes.Count ? boxes[index] : null;
    }

    private void Page_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Only act while a budget cell is being edited.
        var inCell = false;
        for (var node = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is NumberBox) { inCell = true; break; }
        if (!inCell) return;

        // A press on another control (or on the cell itself) is handled by that control.
        for (var node = e.OriginalSource as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is NumberBox or TextBox or ComboBox or ButtonBase or CalendarDatePicker or ListViewBase) return;

        BudgetScroll.Focus(FocusState.Programmatic);
    }

    /// <summary>Right-click (or the menu key) on a budget item's name: jump to its edit screen.</summary>
    private void Line_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: BudgetLine { ItemId: { } itemId } } target) return;

        var menu = new MenuFlyout();
        var edit = new MenuFlyoutItem { Text = "Edit Budget Item" };
        edit.Click += (_, _) => (App.MainWindow as MainWindow)?.NavigateTo("items", itemId);
        menu.Items.Add(edit);

        if (args.TryGetPosition(target, out var point)) menu.ShowAt(target, point);
        else menu.ShowAt(target);
        args.Handled = true;
    }

    // ---- Dragging a budget item to another category ----

    private void Line_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not BudgetLine { IsItem: true } line)
        {
            args.Cancel = true;
            return;
        }
        args.Data.Properties["budgetLine"] = line;
        args.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
    }

    private void Line_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Properties.ContainsKey("budgetLine")) return;
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        e.DragUIOverride.Caption = "Move here";
    }

    /// <summary>Dropped in the top half of a row: before it. In the bottom half: after it. On a heading: first in that category.</summary>
    private async void Line_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BudgetLine target } row) return;
        if (!e.DataView.Properties.TryGetValue("budgetLine", out var dragged) || dragged is not BudgetLine moving) return;
        await ViewModel.MoveLineAsync(moving, target, after: e.GetPosition(row).Y > row.ActualHeight / 2);
    }

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

/// <summary>Remembers, for this run, that the guided setup has already been shown on its own.</summary>
internal static class SetupOffered
{
    public static bool Value { get; set; }
}

/// <summary>Remembers, for this run, that the user chose to start empty.</summary>
internal static class FirstRunDismissed
{
    public static bool Value { get; set; }
}
