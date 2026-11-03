using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpeedBudget.App.Views;
using Windows.System;

namespace SpeedBudget.App;

public sealed partial class MainWindow : Window
{
    public WatcherService Watcher => AppServices.Watcher;

    public MainWindow()
    {
        InitializeComponent();
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);

        // Ctrl+1 to Ctrl+6 jump to each page in the menu.
        for (var i = 0; i < Nav.MenuItems.Count && i < 9; i++)
        {
            var index = i;
            var accelerator = new KeyboardAccelerator { Key = VirtualKey.Number1 + index, Modifiers = VirtualKeyModifiers.Control };
            accelerator.Invoked += (_, args) => { Nav.SelectedItem = Nav.MenuItems[index]; args.Handled = true; };
            Nav.KeyboardAccelerators.Add(accelerator);
        }

        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.IsSettingsSelected ? "Settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string;
        switch (tag)
        {
            case "Budget": ContentFrame.Navigate(typeof(BudgetPage)); break;
            case "Transactions": ContentFrame.Navigate(typeof(TransactionsPage)); break;
            case "Accounts": ContentFrame.Navigate(typeof(AccountsPage)); break;
            case "Reconcile": ContentFrame.Navigate(typeof(ReconcilePage)); break;
            case "Payments": ContentFrame.Navigate(typeof(PaymentsPage)); break;
            case "Items": ContentFrame.Navigate(typeof(ItemsPage)); break;
            case "Settings": ContentFrame.Navigate(typeof(SettingsPage)); break;
        }
    }
}
