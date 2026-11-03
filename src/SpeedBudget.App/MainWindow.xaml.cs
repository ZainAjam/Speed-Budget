using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpeedBudget.App.ViewModels;
using SpeedBudget.App.Views;
using SpeedBudget.Core;
using Windows.System;

namespace SpeedBudget.App;

public sealed partial class MainWindow : Window
{
    private readonly NavigationViewItem _transactions = new() { Content = "Transactions", Tag = "tx", Icon = new SymbolIcon(Symbol.List), IsExpanded = true };

    private bool _onTransactions;

    public WatcherService Watcher => AppServices.Watcher;

    public MainWindow()
    {
        InitializeComponent();
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);

        var budget = new NavigationViewItem { Content = "Budget", Tag = "budget", Icon = new SymbolIcon(Symbol.Calculator) };
        var accounts = new NavigationViewItem { Content = "Accounts", Tag = "accounts", Icon = new SymbolIcon(Symbol.Library) };
        var payees = new NavigationViewItem { Content = "Counterparties", Tag = "payees", Icon = new SymbolIcon(Symbol.People) };
        var items = new NavigationViewItem { Content = "Manage Budget Items", Tag = "items", Icon = new SymbolIcon(Symbol.Edit) };

        Nav.MenuItems.Add(new NavigationViewItemHeader { Content = "Activity" });
        Nav.MenuItems.Add(budget);
        Nav.MenuItems.Add(_transactions);
        Nav.MenuItems.Add(new NavigationViewItemSeparator());
        Nav.MenuItems.Add(new NavigationViewItemHeader { Content = "Settings" });
        Nav.MenuItems.Add(accounts);
        Nav.MenuItems.Add(payees);
        Nav.MenuItems.Add(items);
        Nav.FooterMenuItems.Add(new NavigationViewItem { Content = "Backup and export", Tag = "settings", Icon = new SymbolIcon(Symbol.Setting) });

        // Ctrl+1 to Ctrl+5 jump to Budget, Transactions, Accounts, Counterparties and Manage Budget Items.
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

        RebuildAccountPages();
        AppServices.AccountsChanged += () => DispatcherQueue.TryEnqueue(RebuildAccountPages);
        Nav.SelectedItem = budget;
    }

    /// <summary>Lists "All accounts" and each open account under Transactions, so each can be viewed on its own.</summary>
    private async void RebuildAccountPages()
    {
        var selectedTag = (Nav.SelectedItem as NavigationViewItem)?.Tag as string;
        var accounts = (await AppServices.Store.GetAccountsAsync()).Where(a => !a.IsClosed).ToList();

        _transactions.MenuItems.Clear();
        _transactions.MenuItems.Add(new NavigationViewItem { Content = "All accounts", Tag = "tx:all", Icon = new SymbolIcon(Symbol.AllApps) });
        foreach (var account in accounts)
            _transactions.MenuItems.Add(new NavigationViewItem { Content = account.Name, Tag = $"tx:{account.Id}", Icon = new SymbolIcon(Symbol.Contact2) });

        // Keep the same page selected, or fall back to the Transactions heading if its account went away.
        if (selectedTag is not null && selectedTag.StartsWith("tx:"))
        {
            var again = _transactions.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == selectedTag);
            Nav.SelectedItem = again ?? _transactions;
        }
    }

    // The strip is always there on the transaction pages (even when empty, so the first item can be added), and
    // nowhere else.
    private void UpdateWatcherStrip()
    {
        WatcherHost.Visibility = _onTransactions ? Visibility.Visible : Visibility.Collapsed;
        UpdateAddButton();
    }

    private void WatcherHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateAddButton();

    private void ChipList_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateAddButton();

    /// <summary>
    /// The + is offered for as long as there is room for another chip after it: the space left beside the chips has
    /// to hold the button and a typical chip. With no chips yet it is always there, with a label saying what it is for.
    /// </summary>
    private void UpdateAddButton()
    {
        var count = Watcher.Chips.Count;
        AddChipLabel.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var available = WatcherHost.ActualWidth - WatcherHost.Padding.Left - WatcherHost.Padding.Right;
        var used = count == 0 ? 0 : ChipList.ActualWidth;
        var typicalChip = count > 0 && used > 0 ? used / count : 190;
        const double addButton = 44;

        AddChipButton.Visibility = count == 0 || used + addButton + typicalChip <= available ? Visibility.Visible : Visibility.Collapsed;
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
