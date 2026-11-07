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
    // "Transactions" is only a label for the accounts listed under it: it cannot be selected, and pressing it does nothing.
    private readonly NavigationViewItem _transactions = new() { Tag = "heading", SelectsOnInvoked = false, IsHitTestVisible = false, IsTabStop = false };

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

        var budget = new NavigationViewItem { Content = "Plan your spending", Tag = "budget", Icon = new SymbolIcon(Symbol.Calculator) };

        _transactions.Content = HeadingLabel("Transactions");
        Nav.MenuItems.Add(Heading("Budget"));
        Nav.MenuItems.Add(budget);
        Nav.MenuItems.Add(_transactions);
        Nav.MenuItems.Add(_addAccountItem);
        Nav.ItemInvoked += (sender, args) =>
        {
            var tag = args.InvokedItemContainer?.Tag as string;
            if (tag == "add-account") _ = AddAccountAsync();
            else if (tag == "zoom" && !Nav.IsPaneOpen) ShowZoomFlyout();   // in the narrow strip the icon opens the controls
        };

        // Zoom: while the panel is open the buttons sit right in it; when it is narrow only the icon shows (and opens them in a small box).
        _zoomItem = new NavigationViewItem { Tag = "zoom", SelectsOnInvoked = false, Icon = new FontIcon { Glyph = "\uE8A3" } };
        _zoomItem.Content = BuildZoomControls(inFlyout: false);
        Nav.FooterMenuItems.Add(_zoomItem);
        Nav.FooterMenuItems.Add(new NavigationViewItemSeparator());   // a line sets App Settings apart from the zoom
        Nav.FooterMenuItems.Add(new NavigationViewItem { Content = "App Settings", Tag = "settings", Icon = new SymbolIcon(Symbol.Setting) });

        // The available balance beside each account follows every change to the data.
        _balanceTimer = DispatcherQueue.CreateTimer();
        _balanceTimer.Interval = TimeSpan.FromMilliseconds(300);
        _balanceTimer.IsRepeating = false;
        _balanceTimer.Tick += (_, _) => _ = RefreshBalancesAsync();
        AppServices.Db.SavedChanges += (_, _) => DispatcherQueue.TryEnqueue(() => { _balanceTimer.Stop(); _balanceTimer.Start(); });

        // Ctrl+1 and Ctrl+2 jump to Budget and Transactions.
        var shortcuts = new[] { budget, (NavigationViewItem?)null };   // the second one is the first account in the list
        for (var i = 0; i < shortcuts.Length; i++)
        {
            var target = shortcuts[i];
            var accelerator = new KeyboardAccelerator { Key = VirtualKey.Number1 + i, Modifiers = VirtualKeyModifiers.Control };
            accelerator.Invoked += (_, args) => { Nav.SelectedItem = target ?? _accountItems.FirstOrDefault() ?? (object)budget; args.Handled = true; };
            Nav.KeyboardAccelerators.Add(accelerator);
        }

        // Ctrl+Z undoes the last change to anything you can edit, on any screen; Ctrl+Y (or Ctrl+Shift+Z) puts it back.
        foreach (var (key, mods, redo) in new[] { (VirtualKey.Z, VirtualKeyModifiers.Control, false), (VirtualKey.Y, VirtualKeyModifiers.Control, true),
                     (VirtualKey.Z, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, true) })
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = mods };
            accelerator.Invoked += async (_, args) =>
            {
                // While typing in a box, Ctrl+Z undoes the typing, as it does everywhere else.
                if (FocusManager.GetFocusedElement(Nav.XamlRoot) is TextBox { IsReadOnly: false } box && (redo ? box.CanRedo : box.CanUndo)) return;
                args.Handled = true;
                await UndoAsync(redo);
            };
            Nav.KeyboardAccelerators.Add(accelerator);
        }

        // Zoom: Ctrl and + or - (or the mouse wheel) zooms in and out, Ctrl+0 goes back to 100%.
        foreach (var (key, mods, direction) in new[]
                 {
                     (VirtualKey.Add, VirtualKeyModifiers.Control, 1), ((VirtualKey)187, VirtualKeyModifiers.Control, 1), ((VirtualKey)187, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, 1),
                     (VirtualKey.Subtract, VirtualKeyModifiers.Control, -1), ((VirtualKey)189, VirtualKeyModifiers.Control, -1),
                     (VirtualKey.Number0, VirtualKeyModifiers.Control, 0), (VirtualKey.NumberPad0, VirtualKeyModifiers.Control, 0),
                 })
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = mods };
            accelerator.Invoked += async (_, args) => { args.Handled = true; await AppServices.StepZoomAsync(direction); };
            Nav.KeyboardAccelerators.Add(accelerator);
        }
        ZoomOuter.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ZoomOuter_Wheel), handledEventsToo: true);
        AppServices.ZoomChanged += () => DispatcherQueue.TryEnqueue(() =>
        {
            ApplyZoom();
            foreach (var level in _zoomLevels) level.Text = $"{Math.Round(AppServices.Zoom * 100)}%";
        });
        Nav.Loaded += (_, _) => ApplyZoom();
        ApplyZoom();

        // The pinned-items strip belongs with the transactions, where you are spending, so it shows nowhere else.
        Watcher.PropertyChanged += (_, _) => UpdateWatcherStrip();
        Watcher.Chips.CollectionChanged += (_, _) => UpdateAddButton();

        Nav.PaneOpening += (_, _) => ShowHeadings(true);
        Nav.PaneClosing += (_, args) =>
        {
            // The navigation view closes its pane when something in it is pressed; pressing a zoom button must not close it.
            if (_zoomHold || _paneHeld) { args.Cancel = true; return; }
            ShowHeadings(false);
        };
        // The left panel either always shows, or (Auto Hide Side Panel) shrinks to its icons and opens while the pointer is over it.
        ApplyPanelMode();
        AppServices.InterfaceChanged += () => DispatcherQueue.TryEnqueue(ApplyPanelMode);
        Nav.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(Nav_PointerMoved), true);
        Nav.PointerEntered += (_, _) => _pointerInNav = true;
        Nav.PointerExited += (_, _) =>
        {
            _pointerInNav = false;
            if (AppServices.PanelMode == "hover" && !_paneHeld && !_zoomHold) SetPane(false);
        };

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

    private void ZoomOuter_Wheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;
        var delta = e.GetCurrentPoint(ZoomOuter).Properties.MouseWheelDelta;
        if (delta == 0) return;
        e.Handled = true;
        _ = AppServices.StepZoomAsync(delta > 0 ? 1 : -1);
    }

    private void ZoomOuter_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyZoom();

    /// <summary>
    /// Zooms the page area only (the budget and the transactions): it is scaled, and given the size that makes it exactly fill the room.
    /// The left panel is never scaled, so it looks and behaves the same at every zoom.
    /// </summary>
    private void ApplyZoom()
    {
        var zoom = AppServices.Zoom;
        ContentScale.ScaleX = ContentScale.ScaleY = zoom;
        if (ContentZoomOuter.ActualWidth > 0 && ContentZoomOuter.ActualHeight > 0)
        {
            ContentZoomHost.Width = ContentZoomOuter.ActualWidth / zoom;
            ContentZoomHost.Height = ContentZoomOuter.ActualHeight / zoom;
        }
    }

    private readonly SemaphoreSlim _undoGate = new(1, 1);
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _toastTimer;

    private void ShowToast(string text)
    {
        UndoToastText.Text = text;
        UndoToast.Visibility = Visibility.Visible;
        _toastTimer ??= DispatcherQueue.CreateTimer();
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(3);
        _toastTimer.IsRepeating = false;
        _toastTimer.Tick -= HideToast;
        _toastTimer.Tick += HideToast;
        _toastTimer.Start();
    }

    private void HideToast(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        UndoToast.Visibility = Visibility.Collapsed;
    }

    /// <summary>Undoes (or redoes) the last change to the data, then has the page on show read it again.</summary>
    private async Task UndoAsync(bool redo)
    {
        Diagnostics.Note(redo ? "redo" : "undo");
        if (!await _undoGate.WaitAsync(0)) return;
        try
        {
            var store = AppServices.Store;
            if (store.HasUnsavedEdits) { ShowToast("Finish or cancel the edit you are making first."); return; }
            if (redo ? !store.CanRedo : !store.CanUndo) { ShowToast(redo ? "Nothing to redo." : "Nothing to undo."); return; }

            var result = await store.UndoAsync(redo);
            if (result is null) return;
            if (result.TouchedAccounts) AppServices.RaiseAccountsChanged();
            if (ContentFrame.Content is IReloadable page) await page.ReloadAsync();
            AppServices.Watcher.Refresh();
            ShowToast($"{(redo ? "Redid" : "Undid")} {result.Description}.");
        }
        catch (Exception ex) { ShowToast($"Could not {(redo ? "redo" : "undo")}: {ex.Message}"); }
        finally { _undoGate.Release(); }
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
        if (_pendingUpdate?.Info is not { } info || UpdateButton.Visibility == Visibility.Collapsed) return;
        // The update is under way, so the buttons are no longer needed.
        UpdateButton.Visibility = Visibility.Collapsed;
        WhatsChangedLink.Visibility = Visibility.Collapsed;
        UpdateBar.IsClosable = false;
        try
        {
            UpdateBar.Message = "Downloading the update. Speed Budget will restart when it is ready.";
            BackupService.CreateBackup(AppServices.Db, AppServices.DataDirectory);
            await AppUpdates.DownloadAndRestartAsync(info);
        }
        catch (Exception ex)
        {
            UpdateBar.Message = $"The update failed: {ex.Message}";
            UpdateButton.Visibility = Visibility.Visible;   // so it can be tried again
            WhatsChangedLink.Visibility = Visibility.Visible;
            UpdateBar.IsClosable = true;
        }
    }

    private void ApplyPanelMode()
    {
        ShowHeadings(AppServices.PanelMode == "show");
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
        var window = e.GetCurrentPoint(null).Position;
        _lastPointer = window;
        if (_zoomHold)
        {
            // Zooming moves the buttons away from under the pointer; the panel stays open until the pointer is moved on purpose.
            if (Math.Abs(window.X - _zoomHoldAt.X) + Math.Abs(window.Y - _zoomHoldAt.Y) < 40) return;
            _zoomHold = false;
        }
        if (AppServices.PanelMode != "hover") return;
        var x = e.GetCurrentPoint(Nav).Position.X;
        if (!Nav.IsPaneOpen && x <= Nav.CompactPaneLength) SetPane(true);
        else if (Nav.IsPaneOpen && x > Nav.OpenPaneLength && !_paneHeld) SetPane(false);
    }

    private bool _paneChanging;

    /// <summary>
    /// Opens or closes the left panel from a pointer event. The change is made a moment later rather than inside the event: the
    /// panel refuses (with an "Unspecified error") when it is asked while it is still moving, which used to close the whole app
    /// when the pointer was moved quickly over the panel. A refusal is noted and ignored, and the next pointer move tries again.
    /// </summary>
    private void SetPane(bool open)
    {
        if (_paneChanging || Nav.IsPaneOpen == open) return;
        _paneChanging = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            try { if (Nav.IsPaneOpen != open) Nav.IsPaneOpen = open; }
            catch (Exception ex) { Diagnostics.Note($"The left panel could not be {(open ? "opened" : "closed")}: {ex.Message}"); }
            finally { _paneChanging = false; }
        });
    }

    /// <summary>A section title in the left panel: bold, blue and a little larger than the items, so it reads as a heading.</summary>
    // It is an ordinary row (not a NavigationViewItemHeader) so that it keeps its place when the panel is collapsed to icons:
    // every icon then sits in the same spot whether the panel is open or not. It cannot be selected or clicked.
    private NavigationViewItem Heading(string text) =>
        new() { Content = HeadingLabel(text), Tag = "heading", SelectsOnInvoked = false, IsHitTestVisible = false, IsTabStop = false };

    /// <summary>The text of a section title: bold and blue. It is hidden while the panel is a strip of icons, where it would be cut off.</summary>
    private TextBlock HeadingLabel(string text)
    {
        var label = new TextBlock
        {
            Text = text, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
        };
        _headingLabels.Add(label);
        return label;
    }

    private readonly List<TextBlock> _headingLabels = [];

    /// <summary>The section titles show only while the panel is open: in the narrow strip they would be cut off.</summary>
    private void ShowHeadings(bool open)
    {
        foreach (var label in _headingLabels) label.Opacity = open || AppServices.PanelMode == "show" ? 1 : 0;
    }

    private readonly List<NavigationViewItem> _accountItems = [];

    // The entry at the bottom of the Transactions list that adds a bank account, and the account to open once the list has been rebuilt.
    private readonly NavigationViewItem _addAccountItem = new()
    {
        Content = "Add bank account", Tag = "add-account", SelectsOnInvoked = false, Icon = new SymbolIcon(Symbol.Add),
    };
    private Guid? _openAfterRebuild;

    /// <summary>The Add bank account entry: asks for the details of a new account, adds it and opens it.</summary>
    private async Task AddAccountAsync()
    {
        Diagnostics.Note("add bank account");
        var dialog = new AccountDialog(null) { XamlRoot = Nav.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var created = await AppServices.Store.AddAccountAsync(dialog.AccountName, dialog.Type, dialog.IsOnBudget, dialog.OpeningCents);
            _openAfterRebuild = created.Id;
            AppServices.RaiseAccountsChanged();
        }
        catch (Exception ex) { ShowToast($"Could not add the account: {ex.Message}"); }
    }

    // While an account's menu is open the left panel stays open, even though the pointer has left it for the menu.
    private bool _paneHeld;
    private bool _pointerInNav;

    /// <summary>
    /// Right-click on an account in the left panel: edit it, and then delete it (or close it, when it has transactions, or reopen it,
    /// when it is already closed).
    /// </summary>
    private async void AccountMenu(FrameworkElement row, AccountEntity account, ContextRequestedEventArgs args)
    {
        args.Handled = true;
        var atPointer = args.TryGetPosition(row, out var point);
        _paneHeld = true;
        Diagnostics.Note($"account menu opened on {account.Name}");

        var hasTransactions = true;
        try { hasTransactions = await AppServices.Store.AccountHasTransactionsAsync(account.Id); }
        catch { /* treated as having transactions: closing is the safe choice */ }

        var menu = new MenuFlyout();
        menu.Closed += (_, _) =>
        {
            _paneHeld = false;
            DispatcherQueue.TryEnqueue(() => { if (AppServices.PanelMode == "hover" && !_pointerInNav) SetPane(false); });
        };

        var edit = new MenuFlyoutItem { Text = "Edit account…", Icon = new FontIcon { Glyph = "\uE70F" } };
        edit.Click += async (_, _) => await EditAccountAsync(account.Id);
        menu.Items.Add(edit);

        if (account.IsClosed)
        {
            var reopen = new MenuFlyoutItem { Text = "Reopen account", Icon = new FontIcon { Glyph = "\uE777" } };
            reopen.Click += async (_, _) => await ReopenAccountAsync(account.Id);
            menu.Items.Add(reopen);
        }
        if (!account.IsClosed || !hasTransactions)
        {
            var remove = new MenuFlyoutItem
            {
                Text = hasTransactions ? "Close account…" : "Delete account…",
                Icon = new FontIcon { Glyph = hasTransactions ? "\uE72E" : "\uE74D" },
            };
            remove.Click += async (_, _) => await DeleteAccountAsync(account.Id, hasTransactions);
            menu.Items.Add(remove);
        }

        if (atPointer) menu.ShowAt(row, point);
        else menu.ShowAt(row);
    }

    private async Task ReopenAccountAsync(Guid accountId)
    {
        var account = (await AppServices.Store.GetAccountsAsync()).FirstOrDefault(a => a.Id == accountId);
        if (account is null) return;
        try
        {
            await AppServices.Store.UpdateAccountAsync(accountId, account.Name, account.Type, account.IsOnBudget, account.OpeningBalance, isClosed: false);
            AppServices.RaiseAccountsChanged();
            ShowToast($"Reopened {account.Name}.");
        }
        catch (Exception ex) { ShowToast($"Could not reopen the account: {ex.Message}"); }
    }

    private async Task EditAccountAsync(Guid accountId)
    {
        var account = (await AppServices.Store.GetAccountsAsync()).FirstOrDefault(a => a.Id == accountId);
        if (account is null) return;
        var dialog = new AccountDialog(account) { XamlRoot = Nav.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            await AppServices.Store.UpdateAccountAsync(accountId, dialog.AccountName, dialog.Type, dialog.IsOnBudget, dialog.OpeningCents, dialog.IsClosed);
            AppServices.RaiseAccountsChanged();
        }
        catch (Exception ex) { ShowToast($"Could not save the account: {ex.Message}"); }
    }

    private async Task DeleteAccountAsync(Guid accountId, bool hasTransactions)
    {
        var account = (await AppServices.Store.GetAccountsAsync()).FirstOrDefault(a => a.Id == accountId);
        if (account is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = Nav.XamlRoot,
            Title = hasTransactions ? $"Close {account.Name}?" : $"Delete {account.Name}?",
            Content = hasTransactions
                ? "This account has transactions, so it is closed rather than deleted and its history is kept. A closed account stays in the list, dimmed, and you can reopen it from its right-click menu."
                : "This account has no transactions. It is deleted.",
            PrimaryButtonText = hasTransactions ? "Close account" : "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var deleted = await AppServices.Store.DeleteOrCloseAccountAsync(account);
            AppServices.RaiseAccountsChanged();
            ShowToast(deleted ? $"Deleted {account.Name}." : $"Closed {account.Name}.");
        }
        catch (Exception ex) { ShowToast($"Could not delete the account: {ex.Message}"); }
    }

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
        var name = new TextBlock { Text = account.IsClosed ? $"{account.Name} (closed)" : account.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (account.IsClosed) row.Opacity = 0.55;
        Grid.SetColumn(amount, 1);
        row.Children.Add(name);
        row.Children.Add(amount);

        var item = new NavigationViewItem { Content = row, Tag = $"tx:{account.Id}",
            // A bank (building) for an account in the budget; an eye for one that is only being watched.
            Icon = new FontIcon { Glyph = account.IsOnBudget ? "" : "" } };

        // Accounts can be dragged into a new order, within their own kind (budget accounts above tracked ones).
        row.CanDrag = !account.IsClosed;
        row.AllowDrop = !account.IsClosed;
        row.ContextRequested += (_, e) => AccountMenu(row, account, e);
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
            // Open accounts first (budget accounts, then tracked ones, in the order they were dragged into), closed ones after them.
            accounts = (await readStore.GetAccountsAsync()).OrderBy(a => a.IsClosed).ThenByDescending(a => a.IsOnBudget).ThenBy(a => a.SortOrder).ThenBy(a => a.Name).ToList();
            foreach (var account in accounts) balances[account.Id] = (await readStore.GetBalancesAsync(account.Id)).BookBalance;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not list the accounts: {ex.Message}");
            return;
        }
        _trackedAccounts.Clear();
        foreach (var tracked in accounts.Where(a => !a.IsOnBudget && !a.IsClosed)) _trackedAccounts.Add(tracked.Id);
        UpdateWatcherStrip();

        // The accounts are rows of their own under Transactions (not nested in it), so their icons line up with the others.
        foreach (var old in _accountItems) Nav.MenuItems.Remove(old);
        _accountItems.Clear();
        _balanceBlocks.Clear();
        _accountOrder.Clear();
        _accountOrder.AddRange(accounts.Select(a => a.Id));
        var at = Nav.MenuItems.IndexOf(_transactions) + 1;
        foreach (var account in accounts)
        {
            var item = AccountItem(account, balances.GetValueOrDefault(account.Id));
            _accountItems.Add(item);
            Nav.MenuItems.Insert(at++, item);
        }
        Nav.MenuItems.Remove(_addAccountItem);
        Nav.MenuItems.Insert(Math.Min(at, Nav.MenuItems.Count), _addAccountItem);   // the add entry stays at the bottom of the list

        // A new account is opened as soon as it is in the list.
        if (_openAfterRebuild is { } opened && _accountItems.FirstOrDefault(i => (string?)i.Tag == $"tx:{opened}") is { } openedItem)
        {
            _openAfterRebuild = null;
            Nav.SelectedItem = openedItem;
            return;
        }

        // Keep the same page selected, or fall back to the first account if its account went away.
        if (selectedTag is not null && selectedTag.StartsWith("tx:"))
        {
            var again = _accountItems.FirstOrDefault(i => (string?)i.Tag == selectedTag);
            Nav.SelectedItem = again ?? _accountItems.FirstOrDefault() ?? Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == "budget");
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

    private bool _zoomHold;
    private Windows.Foundation.Point _lastPointer, _zoomHoldAt;

    /// <summary>Called when a zoom button in the panel is pressed: the panel stays open even though the zoom moves the buttons off the pointer.</summary>
    private void HoldPaneForZoom()
    {
        _zoomHold = true;
        _zoomHoldAt = _lastPointer;
    }

    private NavigationViewItem _zoomItem = null!;
    private readonly List<TextBlock> _zoomLevels = [];

    /// <summary>
    /// The zoom controls: smaller, the level (click it to type a percentage from 50 to 200: Enter applies, Esc cancels), larger.
    /// The same controls are used in the left panel and in the box that opens from the icon when the panel is narrow.
    /// </summary>
    private StackPanel BuildZoomControls(bool inFlyout)
    {
        var out_ = new Button { Content = new FontIcon { Glyph = "\uE738", FontSize = 14 }, Padding = new Thickness(8, 5, 8, 5) };
        ToolTipService.SetToolTip(out_, "Zoom out (Ctrl and -)");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(out_, "Zoom out");
        out_.Click += async (_, _) => { HoldPaneForZoom(); await AppServices.StepZoomAsync(-1); };
        var in_ = new Button { Content = new FontIcon { Glyph = "\uE710", FontSize = 14 }, Padding = new Thickness(8, 5, 8, 5) };
        ToolTipService.SetToolTip(in_, "Zoom in (Ctrl and +)");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(in_, "Zoom in");
        in_.Click += async (_, _) => { HoldPaneForZoom(); await AppServices.StepZoomAsync(1); };

        var level = new TextBlock { Text = $"{Math.Round(AppServices.Zoom * 100)}%", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _zoomLevels.Add(level);
        var typed = new TextBox { Visibility = Visibility.Collapsed, TextAlignment = TextAlignment.Center, MaxLength = 3, MinHeight = 28, Padding = new Thickness(4, 3, 4, 3), VerticalAlignment = VerticalAlignment.Center };
        var host = new Grid { MinWidth = 56, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        ToolTipService.SetToolTip(host, "Click to type a zoom percentage");
        host.Children.Add(level);
        host.Children.Add(typed);

        var typing = false;
        // A press on the percentage must not reach the panel item underneath: the panel treats that as choosing the item and closes.
        host.PointerPressed += (_, e) => { if (!inFlyout) HoldPaneForZoom(); e.Handled = true; };
        host.PointerReleased += (_, e) => e.Handled = true;
        host.Tapped += (_, e) =>
        {
            e.Handled = true;
            if (typing) return;
            typing = true;
            _paneHeld = true;   // the panel stays open while a value is typed
            typed.Text = $"{Math.Round(AppServices.Zoom * 100)}";
            level.Visibility = Visibility.Collapsed;
            typed.Visibility = Visibility.Visible;
            typed.Focus(FocusState.Programmatic);
            typed.SelectAll();
        };
        async Task Finish(bool apply)
        {
            if (!typing) return;
            typing = false;
            var text = typed.Text.Trim().TrimEnd('%').Trim();
            typed.Visibility = Visibility.Collapsed;
            level.Visibility = Visibility.Visible;
            if (apply && int.TryParse(text, out var percent)) { if (!inFlyout) HoldPaneForZoom(); await AppServices.SetZoomAsync(Math.Clamp(percent, 50, 200) / 100.0); }
            level.Text = $"{Math.Round(AppServices.Zoom * 100)}%";
            if (!inFlyout)
            {
                _paneHeld = false;
                if (AppServices.PanelMode == "hover" && !_pointerInNav && !_zoomHold) SetPane(false);
            }
        }
        typed.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; await Finish(true); }
            else if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; await Finish(false); }
        };
        typed.LostFocus += async (_, _) => await Finish(true);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(out_);
        row.Children.Add(host);
        row.Children.Add(in_);
        return row;
    }

    /// <summary>With the left panel narrow, the zoom icon opens the controls in a small box beside it.</summary>
    private void ShowZoomFlyout()
    {
        var row = BuildZoomControls(inFlyout: true);
        var level = _zoomLevels[^1];
        var flyout = new Flyout { Content = row, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.RightEdgeAlignedBottom };
        _paneHeld = true;
        flyout.Closed += (_, _) =>
        {
            _zoomLevels.Remove(level);
            _paneHeld = false;
            DispatcherQueue.TryEnqueue(() => { if (AppServices.PanelMode == "hover" && !_pointerInNav) SetPane(false); });
        };
        flyout.ShowAt(_zoomItem);
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
        Diagnostics.Note($"open page {tag}");
        _onTransactions = tag is not null && tag.StartsWith("tx");
        _accountPage = tag is not null && tag.StartsWith("tx:") && Guid.TryParse(tag.AsSpan(3), out var page) ? page : null;
        UpdateWatcherStrip();
        switch (tag)
        {
            case "budget": ContentFrame.Navigate(typeof(BudgetPage), parameter); break;
            case "tx" or "tx:all": ContentFrame.Navigate(typeof(TransactionsPage)); break;
            case "settings": ContentFrame.Navigate(typeof(SettingsPage)); break;
            case { } t when t.StartsWith("tx:") && Guid.TryParse(t.AsSpan(3), out var id):
                ContentFrame.Navigate(typeof(TransactionsPage), id);
                break;
        }
    }
}
