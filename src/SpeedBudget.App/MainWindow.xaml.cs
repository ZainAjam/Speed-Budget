using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpeedBudget.App.Views;
using Windows.System;

namespace SpeedBudget.App;

public sealed partial class MainWindow : Window
{
    private readonly NavigationViewItem _transactions = new() { Content = "Transactions", Tag = "tx", Icon = new SymbolIcon(Symbol.List), IsExpanded = true };

    public WatcherService Watcher => AppServices.Watcher;

    public MainWindow()
    {
        InitializeComponent();
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);

        var budget = new NavigationViewItem { Content = "Budget", Tag = "budget", Icon = new SymbolIcon(Symbol.Calculator) };
        var accounts = new NavigationViewItem { Content = "Accounts", Tag = "accounts", Icon = new SymbolIcon(Symbol.Library) };
        var payees = new NavigationViewItem { Content = "Payees", Tag = "payees", Icon = new SymbolIcon(Symbol.People) };
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

        // Ctrl+1 to Ctrl+5 jump to Budget, Transactions, Accounts, Payees and Manage Budget Items.
        var shortcuts = new[] { budget, _transactions, accounts, payees, items };
        for (var i = 0; i < shortcuts.Length; i++)
        {
            var target = shortcuts[i];
            var accelerator = new KeyboardAccelerator { Key = VirtualKey.Number1 + i, Modifiers = VirtualKeyModifiers.Control };
            accelerator.Invoked += (_, args) => { Nav.SelectedItem = target; args.Handled = true; };
            Nav.KeyboardAccelerators.Add(accelerator);
        }

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

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        switch (tag)
        {
            case "budget": ContentFrame.Navigate(typeof(BudgetPage)); break;
            case "tx" or "tx:all": ContentFrame.Navigate(typeof(TransactionsPage)); break;
            case "accounts": ContentFrame.Navigate(typeof(AccountsPage)); break;
            case "payees": ContentFrame.Navigate(typeof(PayeesPage)); break;
            case "items": ContentFrame.Navigate(typeof(ItemsPage)); break;
            case "settings": ContentFrame.Navigate(typeof(SettingsPage)); break;
            case { } t when t.StartsWith("tx:") && Guid.TryParse(t.AsSpan(3), out var id):
                ContentFrame.Navigate(typeof(TransactionsPage), id);
                break;
        }
    }
}
