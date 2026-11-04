using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpeedBudget.App.ViewModels;
using SpeedBudget.App.Views;
using SpeedBudget.Core;
using SpeedBudget.Data;
using Windows.System;

namespace SpeedBudget.App;

public sealed partial class MainWindow : Window
{
    private readonly NavigationViewItem _transactions = new() { Content = "Transactions", Tag = "tx", Icon = new SymbolIcon(Symbol.List), IsExpanded = true };

    private bool _onTransactions;
    private Guid? _accountPage;
    private readonly HashSet<Guid> _trackedAccounts = [];

    public WatcherService Watcher => AppServices.Watcher;

    public MainWindow()
    {
        InitializeComponent();
        WindowPlacement.Attach(this);
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);

        var budget = new NavigationViewItem { Content = "Budget", Tag = "budget", Icon = new SymbolIcon(Symbol.Calculator) };
        var accounts = new NavigationViewItem { Content = "Accounts", Tag = "accounts", Icon = new SymbolIcon(Symbol.Library) };
        var payees = new NavigationViewItem { Content = "Counterparties", Tag = "payees", Icon = new SymbolIcon(Symbol.People) };
        var items = new NavigationViewItem { Content = "Budget Items", Tag = "items", Icon = new SymbolIcon(Symbol.Edit) };

        Nav.MenuItems.Add(Heading("Activity"));
        Nav.MenuItems.Add(budget);
        Nav.MenuItems.Add(_transactions);
        Nav.MenuItems.Add(new NavigationViewItemSeparator());
        Nav.MenuItems.Add(Heading("Configurations"));
        Nav.MenuItems.Add(accounts);
        Nav.MenuItems.Add(payees);
        Nav.MenuItems.Add(items);
        Nav.FooterMenuItems.Add(new NavigationViewItem { Content = "App Settings", Tag = "settings", Icon = new SymbolIcon(Symbol.Setting) });

        // The available balance beside each account follows every change to the data.
        _balanceTimer = DispatcherQueue.CreateTimer();
        _balanceTimer.Interval = TimeSpan.FromMilliseconds(300);
        _balanceTimer.IsRepeating = false;
        _balanceTimer.Tick += (_, _) => _ = RefreshBalancesAsync();
        AppServices.Db.SavedChanges += (_, _) => DispatcherQueue.TryEnqueue(() => { _balanceTimer.Stop(); _balanceTimer.Start(); });

        // Ctrl+1 to Ctrl+5 jump to Budget, Transactions, Accounts, Counterparties and Budget Items.
        var shortcuts = new[] { budget, _transactions, accounts, payees, items };
        for (var i = 0; i < shortcuts.Length; i++)
        {
            var target = shortcuts[i];
            var accelerator = new KeyboardAccelerator { Key = VirtualKey.Number1 + i, Modifiers = VirtualKeyModifiers.Control };
            accelerator.Invoked += (_, args) => { Nav.SelectedItem = target; args.Handled = true; };
            Nav.KeyboardAccelerators.Add(accelerator);
        }

        // The pinned-items strip belongs with the transactions, where you are spending, so it shows nowhere else.
        Watcher.PropertyChanged += (_, _) => UpdateWatcherStrip();
        Watcher.Chips.CollectionChanged += (_, _) => UpdateAddButton();

        // The left panel either always shows, or (Auto Hide Side Panel) shrinks to its icons and opens while the pointer is over it.
        ApplyPanelMode();
        AppServices.InterfaceChanged += () => DispatcherQueue.TryEnqueue(ApplyPanelMode);
        Nav.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(Nav_PointerMoved), true);
        Nav.PointerExited += (_, _) => { if (AppServices.PanelMode == "hover") Nav.IsPaneOpen = false; };

        RebuildAccountPages();
        AppServices.AccountsChanged += () => DispatcherQueue.TryEnqueue(RebuildAccountPages);
        Nav.SelectedItem = budget;

        // Look when the app opens, then every few hours, for people who leave it running.
        _ = CheckForUpdateAsync();
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromHours(6);
        timer.Tick += (_, _) => _ = CheckForUpdateAsync();
        timer.Start();
    }

    private UpdateCheck? _pendingUpdate;

    /// <summary>Quietly looks for a newer version, and says so only if there is one.</summary>
    private async Task CheckForUpdateAsync()
    {
        try
        {
            var check = await AppUpdates.CheckAsync();
            if (check.State != UpdateState.Available || UpdateBar.IsOpen) return;
            _pendingUpdate = check;
            UpdateBar.Message = $"Speed Budget {check.NewVersion} is available. Your data is kept as it is.";
            UpdateBar.IsOpen = true;
        }
        catch { /* an update check must never get in the way of opening the app */ }
    }

    /// <summary>The banner's "What's changed": read the notes before downloading, and update from the same dialog if they look good.</summary>
    private async void WhatsChanged_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is not { } pending) return;
        var dialog = new UpdateNotesDialog(pending.NewVersion ?? "", pending.Notes) { XamlRoot = Nav.XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) UpdateNow_Click(sender, e);
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate?.Info is not { } info) return;
        try
        {
            UpdateBar.Message = "Downloading the update. Speed Budget will restart when it is ready.";
            BackupService.CreateBackup(AppServices.Db, AppServices.DataDirectory);
            await AppUpdates.DownloadAndRestartAsync(info);
        }
        catch (Exception ex) { UpdateBar.Message = $"The update failed: {ex.Message}"; }
    }

    private void ApplyPanelMode()
    {
        switch (AppServices.PanelMode)
        {
            case "show":
                Nav.IsPaneToggleButtonVisible = false;
                Nav.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
                Nav.IsPaneOpen = true;
                break;
            case "hide":
                // Collapsed: always the narrow strip of icons, which never opens by itself.
                Nav.IsPaneToggleButtonVisible = false;
                Nav.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;
                Nav.IsPaneOpen = false;
                break;
            default:
                Nav.IsPaneToggleButtonVisible = false;
                Nav.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;
                Nav.IsPaneOpen = false;
                break;
        }
    }

    /// <summary>With "show on hover": the panel opens when the pointer reaches its narrow strip, and closes when the pointer leaves it.</summary>
    private void Nav_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (AppServices.PanelMode != "hover") return;
        var x = e.GetCurrentPoint(Nav).Position.X;
        if (!Nav.IsPaneOpen && x <= Nav.CompactPaneLength) Nav.IsPaneOpen = true;
        else if (Nav.IsPaneOpen && x > Nav.OpenPaneLength) Nav.IsPaneOpen = false;
    }

    /// <summary>A section title in the left panel: bold, blue and a little larger than the items, so it reads as a heading.</summary>
    private static NavigationViewItemHeader Heading(string text) => new()
    {
        Content = text, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
    };

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _balanceTimer;
    private readonly Dictionary<Guid, TextBlock> _balanceBlocks = [];

    private Guid? _draggedAccount;
    private bool _draggedAccountOnBudget;
    private readonly List<Guid> _accountOrder = [];

    /// <summary>Puts a dragged account in the place of the one it was dropped on, and remembers the new order.</summary>
    private async Task MoveAccountAsync(Guid moved, Guid target)
    {
        var order = _accountOrder.ToList();
        var from = order.IndexOf(moved);
        var to = order.IndexOf(target);
        if (from < 0 || to < 0) return;
        order.RemoveAt(from);
        order.Insert(to, moved);
        try
        {
            await AppServices.Store.SetAccountOrderAsync(order);
            AppServices.RaiseAccountsChanged();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not save the account order: {ex.Message}"); }
    }

    /// <summary>An account in the left panel: its name, and its available balance on the right.</summary>
    private NavigationViewItem AccountItem(AccountEntity account, long balance)
    {
        var amount = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        ShowBalance(amount, balance);
        _balanceBlocks[account.Id] = amount;

        var row = new Grid { ColumnSpacing = 8, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock { Text = account.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(amount, 1);
        row.Children.Add(name);
        row.Children.Add(amount);

        var item = new NavigationViewItem { Content = row, Tag = $"tx:{account.Id}",
            // A bank (building) for an account in the budget; an eye for one that is only being watched.
            Icon = new FontIcon { Glyph = account.IsOnBudget ? "" : "" } };

        // Accounts can be dragged into a new order, within their own kind (budget accounts above tracked ones).
        row.CanDrag = true;
        row.AllowDrop = true;
        row.DragStarting += (_, e) =>
        {
            _draggedAccount = account.Id;
            _draggedAccountOnBudget = account.IsOnBudget;
            e.Data.SetText(account.Name);
        };
        row.DragOver += (_, e) =>
        {
            var allowed = _draggedAccount is { } dragged && dragged != account.Id && _draggedAccountOnBudget == account.IsOnBudget;
            e.AcceptedOperation = allowed ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move : Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            if (allowed) e.DragUIOverride.Caption = "Move here";
            e.Handled = true;
        };
        row.Drop += async (_, e) =>
        {
            e.Handled = true;
            if (_draggedAccount is { } dragged && dragged != account.Id && _draggedAccountOnBudget == account.IsOnBudget)
                await MoveAccountAsync(dragged, account.Id);
            _draggedAccount = null;
        };
        row.Tapped += (_, _) => Nav.SelectedItem = item;   // the row takes the click while it is draggable, so it opens the page itself
        row.DropCompleted += (_, _) => _draggedAccount = null;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, account.Name);
        return item;
    }

    private static void ShowBalance(TextBlock block, long balance)
    {
        block.Text = ViewModels.Format.Money(balance);
        block.Foreground = ViewModels.Format.Signed(balance);   // green above zero, red below, normal text at zero
    }

    /// <summary>Brings each account's balance up to date without rebuilding the menu.</summary>
    private async Task RefreshBalancesAsync()
    {
        try
        {
            using var reader = LocalDatabase.OpenForReading();
            var store = new BudgetStore(reader);
            foreach (var (id, block) in _balanceBlocks.ToList())
                ShowBalance(block, (await store.GetBalancesAsync(id)).BookBalance);
        }
        catch { /* the balances are a convenience; a failed refresh leaves the old figures */ }
    }

    /// <summary>Lists each open account under Transactions, so each can be viewed on its own.</summary>
    private async void RebuildAccountPages()
    {
        var selectedTag = (Nav.SelectedItem as NavigationViewItem)?.Tag as string;
        // Read on a context of its own: this runs while pages are loading and saving on the main one, which can only do one thing at a time.
        List<AccountEntity> accounts;
        var balances = new Dictionary<Guid, long>();
        try
        {
            using var reader = LocalDatabase.OpenForReading();
            var readStore = new BudgetStore(reader);
            // Budget accounts come first, then tracked ones; within each, in the order the user has dragged them into.
            accounts = (await readStore.GetAccountsAsync()).Where(a => !a.IsClosed).OrderByDescending(a => a.IsOnBudget).ThenBy(a => a.SortOrder).ThenBy(a => a.Name).ToList();
            foreach (var account in accounts) balances[account.Id] = (await readStore.GetBalancesAsync(account.Id)).BookBalance;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not list the accounts: {ex.Message}");
            return;
        }
        _trackedAccounts.Clear();
        foreach (var tracked in accounts.Where(a => !a.IsOnBudget)) _trackedAccounts.Add(tracked.Id);
        UpdateWatcherStrip();

        _transactions.MenuItems.Clear();
        _balanceBlocks.Clear();
        _accountOrder.Clear();
        _accountOrder.AddRange(accounts.Select(a => a.Id));
        foreach (var account in accounts)
            _transactions.MenuItems.Add(AccountItem(account, balances.GetValueOrDefault(account.Id)));

        // Keep the same page selected, or fall back to the Transactions heading if its account went away.
        if (selectedTag is not null && selectedTag.StartsWith("tx:"))
        {
            var again = _transactions.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == selectedTag);
            Nav.SelectedItem = again ?? _transactions;
        }
    }

    // The strip is always there on the transaction pages of budget accounts (even when empty, so the first item can
    // be added), and nowhere else: not on tracked accounts, which sit outside the budget.
    private void UpdateWatcherStrip()
    {
        WatcherHost.Visibility = _onTransactions && !(_accountPage is { } id && _trackedAccounts.Contains(id)) ? Visibility.Visible : Visibility.Collapsed;
        UpdateAddButton();
    }

    /// <summary>
    /// The + is offered until the strip holds its three items. With no chips yet it carries a label saying what it is for.
    /// </summary>
    private void UpdateAddButton()
    {
        var count = Watcher.Chips.Count;
        AddChipLabel.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddChipButton.Visibility = count < BudgetStore.MaxPinnedItems ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The +: ask which budget item to add to the strip.</summary>
    private async void AddChip_Click(object sender, RoutedEventArgs e)
    {
        var store = AppServices.Store;
        var groups = (await store.GetGroupsAsync()).Where(g => g.Kind == ItemKind.Expense).ToList();
        var items = await store.GetItemsAsync();
        var left = BudgetEngine.Calculate(await store.LoadSnapshotAsync())[^1].Items.ToDictionary(i => i.ItemId, i => i.Remaining);

        var choices = new List<PinChoice>();
        foreach (var group in groups)
            foreach (var item in items.Where(i => i.GroupId == group.Id && !i.IsPinned && !i.IsArchived))
                choices.Add(new PinChoice(item.Id, $"{group.Name} › {item.DisplayName}    {Format.Money(left.GetValueOrDefault(item.Id))}"));

        var dialog = new PinItemDialog(choices) { XamlRoot = Nav.XamlRoot };
        await dialog.ShowAsync();
        if (dialog.Chosen is { } itemId) await store.SetPinnedAsync(itemId, true);
    }

    /// <summary>Right-click (or the menu key) on a chip: offer to take it off the strip.</summary>
    private void Chip_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: Guid itemId } chip) return;

        var menu = new MenuFlyout();
        var remove = new MenuFlyoutItem { Text = "Remove from the watcher strip" };
        remove.Click += async (_, _) => await AppServices.Store.SetPinnedAsync(itemId, false);
        menu.Items.Add(remove);

        if (args.TryGetPosition(chip, out var point)) menu.ShowAt(chip, point);
        else menu.ShowAt(chip);
        args.Handled = true;
    }

    private bool _suppressSelection;

    /// <summary>A pinned-item chip was pressed: show that item on the Budget screen, in the current month.</summary>
    private void Chip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Guid itemId) NavigateTo("budget", itemId);
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressSelection) return;
        Show((args.SelectedItem as NavigationViewItem)?.Tag as string, null);
    }

    /// <summary>
    /// Opens a page from elsewhere in the app, such as the edit screen for one item or counterparty, and moves the
    /// highlight in the left panel to match. The parameter tells the page what to select.
    /// </summary>
    public void NavigateTo(string tag, object? parameter = null)
    {
        if (FindItem(tag, Nav.MenuItems.Concat(Nav.FooterMenuItems)) is { } item)
        {
            _suppressSelection = true;
            Nav.SelectedItem = item;
            _suppressSelection = false;
        }
        Show(tag, parameter);
    }

    private static NavigationViewItem? FindItem(string tag, IEnumerable<object> items)
    {
        foreach (var item in items.OfType<NavigationViewItem>())
        {
            if ((string?)item.Tag == tag) return item;
            if (FindItem(tag, item.MenuItems) is { } child) return child;
        }
        return null;
    }

    private void Show(string? tag, object? parameter)
    {
        _onTransactions = tag is not null && tag.StartsWith("tx");
        _accountPage = tag is not null && tag.StartsWith("tx:") && Guid.TryParse(tag.AsSpan(3), out var page) ? page : null;
        UpdateWatcherStrip();
        switch (tag)
        {
            case "budget": ContentFrame.Navigate(typeof(BudgetPage), parameter); break;
            case "tx" or "tx:all": ContentFrame.Navigate(typeof(TransactionsPage)); break;
            case "accounts": ContentFrame.Navigate(typeof(AccountsPage)); break;
            case "payees": ContentFrame.Navigate(typeof(PayeesPage), parameter); break;
            case "items": ContentFrame.Navigate(typeof(ItemsPage), parameter); break;
            case "settings": ContentFrame.Navigate(typeof(SettingsPage)); break;
            case { } t when t.StartsWith("tx:") && Guid.TryParse(t.AsSpan(3), out var id):
                ContentFrame.Navigate(typeof(TransactionsPage), id);
                break;
        }
    }
}
