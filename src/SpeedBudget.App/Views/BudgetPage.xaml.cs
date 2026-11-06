using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

public sealed partial class BudgetPage : Page, IReloadable
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

    public Task ReloadAsync()
    {
        _revealItem = null;
        return RefreshAsync();
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
            var wizard = new SetupWizardDialog { XamlRoot = XamlRoot };
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

    // ---- Jumping to a month ----

    private void JumpFlyout_Opening(object? sender, object e)
    {
        if (JumpMonthBox.ItemsSource is null)
            JumpMonthBox.ItemsSource = Enumerable.Range(1, 12).Select(m => System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)).ToList();

        // Start from the month now at the left of the screen.
        var month = ViewModel.LeftmostMonth ?? DateOnly.FromDateTime(DateTime.Today);
        JumpMonthBox.SelectedIndex = month.Month - 1;
        JumpYearBox.Value = month.Year;
        JumpMessage.Visibility = Visibility.Collapsed;
    }

    private void JumpGo_Click(object sender, RoutedEventArgs e)
    {
        if (JumpMonthBox.SelectedIndex < 0 || double.IsNaN(JumpYearBox.Value)) return;
        var month = new DateOnly((int)JumpYearBox.Value, JumpMonthBox.SelectedIndex + 1, 1);
        if (ViewModel.JumpToMonth(month)) RangeButton.Flyout.Hide();
        else
        {
            JumpMessage.Text = $"{Core.MonthLabels.For(month)} is not in your budget. Use the left arrow to add earlier months.";
            JumpMessage.Visibility = Visibility.Visible;
        }
    }

    private void JumpToCurrent_Click(object sender, RoutedEventArgs e) => ViewModel.JumpToCurrentMonth();

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

    /// <summary>The arrow beside "Available to budget": shows or hides the monthly figures above it. Folding them is remembered and stops scrolling from opening them; opening them lets scrolling fold and open them again.</summary>
    private async void SummaryToggle_Click(object sender, RoutedEventArgs e)
    {
        var expanded = !SummaryView.Expanded;
        ViewModel.SetSummaryExpanded(expanded);
        SummaryView.UserCollapsed = !expanded;   // folding it is a choice that scrolling never undoes; opening it hands it back to scrolling
        try { await AppServices.Store.SetLocalSettingAsync("budgetSummary", expanded ? "1" : "0"); }
        catch { /* the choice still applies for this run */ }
    }

    // ---- Editing budgeted amounts from the keyboard ----

    /// <summary>On entering a cell: remember its value for Esc. A click leaves the cursor at the end of the amount; the keyboard selects it.</summary>
    private void Budgeted_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not NumberBox box) return;
        box.Tag = box.Value;
        if (Money.FindTextBox(box) is not { } input) return;

        // Clicking a cell puts the cursor at the end of the amount; arriving by keyboard selects it so typing replaces it.
        if (box.FocusState != FocusState.Keyboard)
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { input.SelectionStart = input.Text.Length; input.SelectionLength = 0; });
        else input.SelectAll();
    }

    /// <summary>
    /// Once a section's title (with its column titles) has scrolled out of sight, its column titles take its place under the
    /// month headings: the inflow ones while in the cash inflow rows, the outflow ones once the cash outflow title has gone too.
    /// </summary>
    private void BudgetScroll_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        AutoSummary();
        UpdateFrozenTitles();
    }

    /// <summary>
    /// The monthly summary is open at the top of the page and folds away once it has scrolled out of sight (leaving "Available to
    /// budget" frozen under the month headings), and opens again on returning to the top. The page is moved up by the block's height as
    /// it goes, so what is on screen does not jump.
    /// </summary>
    private void AutoSummary()
    {
        if (SummaryView.UserCollapsed) return;
        var offset = BudgetScroll.VerticalOffset;
        if (SummaryView.Expanded)
        {
            var height = SummaryBlock.ActualHeight + 16;   // the block and the gap after it
            if (height <= 16 || offset <= height + 8) return;
            ViewModel.SetSummaryExpanded(false);
            BudgetScroll.UpdateLayout();
            BudgetScroll.ChangeView(null, offset - height, null, disableAnimation: true);
        }
        else if (offset < 1) ViewModel.SetSummaryExpanded(true);
    }

    private void UpdateFrozenTitles()
    {
        if (InflowTitleRow.ActualHeight == 0 || OutflowTitleRow.ActualHeight == 0) return;
        bool Gone(FrameworkElement row) => row.TransformToVisual(BudgetScroll).TransformPoint(new Windows.Foundation.Point(0, row.ActualHeight)).Y < 0;
        ViewModel.FrozenTitles = Gone(OutflowTitleRow) ? 2 : Gone(InflowTitleRow) ? 1 : 0;
    }

    /// <summary>A cell left blank counts as 0.00: once the box has read what was typed, an empty cell is shown (and saved) as zero.</summary>
    private void Budgeted_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not NumberBox box) return;
        DispatcherQueue.TryEnqueue(() => { if (double.IsNaN(box.Value)) box.Value = 0; });
    }

    /// <summary>
    /// Enter saves the amount and leaves the cell; Esc puts the old amount back and leaves it; Up and Down move between cells. The press is
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
                BudgetScroll.Focus(FocusState.Programmatic);   // leaving the cell is what saves it
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
        if (sender is not FrameworkElement { DataContext: BudgetLine line } target) return;

        var menu = new MenuFlyout();
        if (line.ItemId is { } itemId)
        {
            var rename = new MenuFlyoutItem { Text = "Rename budget item…", Icon = new FontIcon { Glyph = "\uE8AC" } };
            rename.Click += async (_, _) => await RenameItemAsync(itemId);
            var icon = new MenuFlyoutItem { Text = "Change icon…", Icon = new FontIcon { Glyph = "\uE76E" } };
            icon.Click += async (_, _) => await ChangeIconAsync(itemId);
            var delete = new MenuFlyoutItem { Text = "Delete budget item…", Icon = new FontIcon { Glyph = "\uE74D" } };
            delete.Click += async (_, _) => await DeleteItemAsync(itemId, line.Name);
            menu.Items.Add(rename);
            menu.Items.Add(icon);
            menu.Items.Add(delete);
        }
        else if (line.GroupId is { } groupId)
        {
            var rename = new MenuFlyoutItem { Text = "Rename budget group…", Icon = new FontIcon { Glyph = "\uE8AC" } };
            rename.Click += async (_, _) => await RenameGroupAsync(groupId, line.Name);
            var delete = new MenuFlyoutItem { Text = "Delete budget group…", Icon = new FontIcon { Glyph = "\uE74D" } };
            delete.Click += async (_, _) => await DeleteGroupAsync(groupId, line.Name, line.GroupItemIds.Count);
            menu.Items.Add(rename);
            menu.Items.Add(delete);
        }
        else return;

        ShowMenu(menu, target, args);
        args.Handled = true;
    }

    // ---- Adding and renaming budget groups and items ----

    private async void AddIncomeItem_Click(object sender, RoutedEventArgs e) =>
        await AddItemAsync(null, SpeedBudget.Core.ItemKind.Income, "Cash Inflow");

    private async void AddItemToGroup_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is BudgetLine { GroupId: { } groupId } header)
            await AddItemAsync(groupId, SpeedBudget.Core.ItemKind.Expense, header.Name);
    }

    private async Task AddItemAsync(Guid? groupId, SpeedBudget.Core.ItemKind kind, string under)
    {
        var dialog = new NameDialog("New budget item", $"This adds an item under {under}.", "Budget item name", "", "Add",
            iconHeader: "Icon shown in front of the name (optional)") { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await ViewModel.AddItemAsync(groupId, kind, dialog.NameText, dialog.Emoji);
    }

    private async void AddExpenseGroup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NameDialog("New budget group", "A group collects budget items that belong together. Add items to it afterwards with its + button.",
            "Budget group name", "", "Add") { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await ViewModel.AddGroupAsync(dialog.NameText);
    }

    private async Task DeleteItemAsync(Guid itemId, string name)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = $"Delete {name}?",
            Content = "The budget item is deleted. If it already has budget or transactions it is archived instead: it is hidden, and its history is kept.",
            PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.DeleteItemAsync(itemId);
    }

    private async Task DeleteGroupAsync(Guid groupId, string name, int itemCount)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = $"Delete {name}?",
            Content = itemCount == 0 ? "The budget group is deleted."
                : $"The budget group and its {itemCount} budget item{(itemCount == 1 ? "" : "s")} are deleted. An item that already has budget or transactions is archived instead (hidden, with its history kept), and then the group stays.",
            PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.DeleteGroupAsync(groupId, name);
    }

    private async Task RenameGroupAsync(Guid groupId, string current)
    {
        var dialog = new NameDialog("Rename budget group", null, "Budget group name", current, "Rename") { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await ViewModel.RenameGroupAsync(groupId, dialog.NameText);
    }

    private async Task RenameItemAsync(Guid itemId)
    {
        if (await ViewModel.GetItemAsync(itemId) is not { } item) return;
        var dialog = new NameDialog("Rename budget item", null, "Budget item name", item.Name, "Rename") { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await ViewModel.EditItemAsync(itemId, dialog.NameText, null);
    }

    private async Task ChangeIconAsync(Guid itemId)
    {
        if (await ViewModel.GetItemAsync(itemId) is not { } item) return;
        var dialog = new NameDialog("Change icon", null, null, "", "Save", iconHeader: "Icon shown in front of " + item.Name, emoji: item.Emoji ?? "") { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await ViewModel.EditItemAsync(itemId, null, dialog.Emoji);
    }

    // ---- Dragging a budget item to another category ----

    private void Line_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not BudgetLine line)
        {
            args.Cancel = true;
            return;
        }
        args.Data.Properties["budgetLine"] = line;   // an item, or a whole group (by its heading)
        args.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
    }

    private void Line_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Properties.TryGetValue("budgetLine", out var dragged)) return;
        // A group can only be dropped among the other groups of the cash outflow.
        if (dragged is BudgetLine { IsHeader: true } && (sender as FrameworkElement)?.DataContext is not BudgetLine { Group: not null }) return;
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        e.DragUIOverride.Caption = "Move here";
    }

    /// <summary>Dropped in the top half of a row: before it. In the bottom half: after it. On a heading: first in that category.</summary>
    private async void Line_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BudgetLine target } row) return;
        if (!e.DataView.Properties.TryGetValue("budgetLine", out var dragged) || dragged is not BudgetLine moving) return;
        if (moving.IsHeader)
        {
            if (target.Group is not null) await ViewModel.MoveGroupAsync(moving, target);
            return;
        }
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

    // ---- Moving budget between items ----

    /// <summary>Right-click on "Available to budget": give some of it to an item, or cover an overspend from an item with money left.</summary>
    private void Available_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: SummaryCell { IsStrong: true } cell } target) return;
        args.Handled = true;
        if (cell.Cents == 0) return;

        var menu = new MenuFlyout();
        if (cell.Cents > 0)
        {
            var give = new MenuFlyoutItem { Text = "Allocate some of this to a budget item…", Icon = new FontIcon { Glyph = "\uE710" } };
            give.Click += async (_, _) => await AllocateAvailableAsync(cell);
            menu.Items.Add(give);
        }
        else
        {
            var cover = new MenuFlyoutItem { Text = "Cover the shortfall from a budget item…", Icon = new FontIcon { Glyph = "\uE8AB" } };
            cover.Click += async (_, _) => await CoverFromAsync(cell.MonthId, -cell.Cents, toItem: null);
            menu.Items.Add(cover);
        }
        ShowMenu(menu, target, args);
    }

    /// <summary>Right-click on a "Left" figure: move some of what is left to another item (green), or cover the shortfall from one (red).</summary>
    private void Left_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: OutflowCell { Line.IsItem: true } cell } target) return;
        args.Handled = true;
        if (cell.LeftCents == 0) return;

        var menu = new MenuFlyout();
        if (cell.LeftCents > 0)
        {
            var move = new MenuFlyoutItem { Text = "Move some of this to another budget item…", Icon = new FontIcon { Glyph = "\uE8AB" } };
            move.Click += async (_, _) => await MoveLeftAsync(cell);
            menu.Items.Add(move);
        }
        else
        {
            var cover = new MenuFlyoutItem { Text = "Cover the shortfall from another budget item…", Icon = new FontIcon { Glyph = "\uE8AB" } };
            cover.Click += async (_, _) => await CoverFromAsync(cell.MonthId, -cell.LeftCents, toItem: cell.Line.ItemId);
            menu.Items.Add(cover);
        }
        ShowMenu(menu, target, args);
    }

    private static void ShowMenu(MenuFlyout menu, FrameworkElement target, ContextRequestedEventArgs args)
    {
        if (args.TryGetPosition(target, out var point)) menu.ShowAt(target, point);
        else menu.ShowAt(target);
    }

    /// <summary>Gives part of a month's available budget to an item: its budgeted amount goes up by that much.</summary>
    private async Task AllocateAvailableAsync(SummaryCell cell)
    {
        var dialog = new MoveBudgetDialog("Allocate available budget",
            $"{Format.Money(cell.Cents)} is available to budget. Choose the item to give some of it to, and how much.",
            "Budget item", "Allocate", ViewModel.ChoicesFor(cell.MonthId), _ => cell.Cents, cell.Cents) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Chosen is not { } chosen) return;
        await ViewModel.MoveBudgetAsync(cell.MonthId, (chosen.ItemId, dialog.AmountCents));
    }

    /// <summary>Moves part of an item's remaining budget to another item.</summary>
    private async Task MoveLeftAsync(OutflowCell cell)
    {
        var from = cell.Line.ItemId!.Value;
        var dialog = new MoveBudgetDialog($"Move budget from {cell.Line.Name}",
            $"{Format.Money(cell.LeftCents)} is left on {cell.Line.Name}. Choose the item to move some of it to, and how much.",
            "Move it to", "Move", ViewModel.ChoicesFor(cell.MonthId, excluding: from), _ => cell.LeftCents, cell.LeftCents) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Chosen is not { } chosen) return;
        var amount = dialog.AmountCents;
        await ViewModel.MoveBudgetAsync(cell.MonthId, (from, -amount), (chosen.ItemId, amount));
    }

    /// <summary>
    /// Covers a shortfall by taking budget off an item that has some left. With an item given, the shortfall is on that item and
    /// the budget goes to it; without one the shortfall is the month's available budget, so the budget just comes back to it.
    /// </summary>
    private async Task CoverFromAsync(Guid monthId, long shortfall, Guid? toItem)
    {
        var choices = ViewModel.ChoicesFor(monthId, excluding: toItem, onlyWithMoneyLeft: true);
        var dialog = new MoveBudgetDialog("Cover the shortfall",
            $"The shortfall is {Format.Money(shortfall)}. Choose a budget item with money left to take it from, and how much.",
            "Take it from", "Cover", choices, c => Math.Min(shortfall, c.LeftCents), shortfall) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Chosen is not { } chosen) return;
        var amount = dialog.AmountCents;
        if (toItem is { } to) await ViewModel.MoveBudgetAsync(monthId, (chosen.ItemId, -amount), (to, amount));
        else await ViewModel.MoveBudgetAsync(monthId, (chosen.ItemId, -amount));
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
