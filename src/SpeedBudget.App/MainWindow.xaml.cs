using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.Views;

namespace SpeedBudget.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.IsSettingsSelected ? "Settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string;
        switch (tag)
        {
            case "Budget": ContentFrame.Navigate(typeof(BudgetPage)); break;
            case "Accounts": ContentFrame.Navigate(typeof(AccountsPage)); break;
            case "Items": ContentFrame.Navigate(typeof(ItemsPage)); break;
            default: ContentFrame.Navigate(typeof(PlaceholderPage), tag); break;
        }
    }
}
