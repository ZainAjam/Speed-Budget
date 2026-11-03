using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;
using SpeedBudget.Core;

namespace SpeedBudget.App.Views;

public sealed partial class ItemsPage : Page
{
    public ItemsViewModel ViewModel { get; } = new();

    public ItemsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private void Tree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        Note.Text = "";
        if (args.InvokedItem is NodeVm node) ViewModel.Selected = node;
    }

    // The tree rearranges its nodes on a drop but leaves our data alone, so save the new layout.
    private async void Tree_DragItemsCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args) =>
        await ViewModel.SaveStructureAsync(sender.RootNodes);

    private async void AddExpenseGroup_Click(object sender, RoutedEventArgs e) => await ViewModel.AddGroupAsync(ItemKind.Expense);

    private async void AddIncomeGroup_Click(object sender, RoutedEventArgs e) => await ViewModel.AddGroupAsync(ItemKind.Income);

    private async void AddItem_Click(object sender, RoutedEventArgs e) => await ViewModel.AddItemAsync();

    private async void MoveUp_Click(object sender, RoutedEventArgs e) => await ViewModel.MoveAsync(up: true);

    private async void MoveDown_Click(object sender, RoutedEventArgs e) => await ViewModel.MoveAsync(up: false);

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not { } node) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete {node.Name}?",
            Content = "An item that is already used is archived instead, so your history stays correct.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        Note.Text = await ViewModel.DeleteSelectedAsync() ?? "";
    }
}
