using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

/// <summary>The entry form for one new transaction. It edits the page's quick-add fields and saves through the same path.</summary>
public sealed partial class NewTransactionDialog : ContentDialog
{
    public TransactionsViewModel ViewModel { get; }

    public NewTransactionDialog(TransactionsViewModel vm)
    {
        ViewModel = vm;
        InitializeComponent();
    }

    /// <summary>A budget item typed into the drop-down that doesn't exist: ask for its category, create it, and select it.</summary>
    private async void Item_CreateRequested(object? sender, string name)
    {
        var groups = await AppServices.Store.GetGroupsAsync();
        if (groups.Count == 0)
        {
            Error.Text = "Add a budget category first under Budget Items.";
            return;
        }
        var dialog = new NewItemDialog(groups, name) { XamlRoot = XamlRoot };
        Hide();
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var option = await ViewModel.CreateItemAsync(dialog.GroupId, dialog.ItemName);
            if (option is not null) ViewModel.NewItem = option;
        }
        await ShowAsync();
    }

    private async void Payee_CreateRequested(object? sender, string name) => await ViewModel.CreatePayeeAsync(name);

    private async void Add_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Hold the dialog open until the transaction has been saved.
        var deferral = args.GetDeferral();
        var saved = await ViewModel.AddAsync();
        Error.Text = saved ? "" : ViewModel.Message;
        if (!saved) ViewModel.Message = "";
        args.Cancel = !saved;
        deferral.Complete();
    }
}
