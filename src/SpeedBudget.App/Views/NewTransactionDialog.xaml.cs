using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;
using SpeedBudget.Core;

namespace SpeedBudget.App.Views;

/// <summary>The entry form for one new transaction. It edits the page's quick-add fields and saves through the same path.</summary>
public sealed partial class NewTransactionDialog : ContentDialog
{
    public TransactionsViewModel ViewModel { get; }

    public NewTransactionDialog(TransactionsViewModel vm)
    {
        ViewModel = vm;
        InitializeComponent();
        MonthBox.ItemsSource = Enumerable.Range(1, 12).Select(m => CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)).ToList();
        Loaded += async (_, _) =>
        {
            _firstBudgetTransaction = !await AppServices.Store.HasBudgetTransactionsAsync();
            UpdateFirstMonthPanel();
            await MonthChoice.ShowForAsync(DateOnly.FromDateTime(ViewModel.NewDate.DateTime));
        };
        // The panel follows the account chosen (only budget accounts count) and starts on the transaction's own month.
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TransactionsViewModel.NewAccount) or nameof(TransactionsViewModel.NewDate)) UpdateFirstMonthPanel();
            if (e.PropertyName is nameof(TransactionsViewModel.NewDate)) _ = MonthChoice.ShowForAsync(DateOnly.FromDateTime(ViewModel.NewDate.DateTime));
        };
    }

    private bool _firstBudgetTransaction;
    private bool _monthChosenByUser;
    private int _month = DateTime.Today.Month;
    private int _year = DateTime.Today.Year;

    private bool AskForMonth => _firstBudgetTransaction && ViewModel.NewAccount?.IsOnBudget == true;

    private void UpdateFirstMonthPanel()
    {
        FirstMonthPanel.Visibility = AskForMonth ? Visibility.Visible : Visibility.Collapsed;
        if (!AskForMonth || _monthChosenByUser) return;

        // Until the user picks, suggest the month the transaction is dated in.
        var date = ViewModel.NewDate;
        _month = date.Month;
        _year = date.Year;
        _updating = true;
        MonthBox.SelectedIndex = _month - 1;
        YearBox.Value = _year;
        _updating = false;
    }

    private bool _updating;

    private void MonthBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || MonthBox.SelectedIndex < 0) return;
        _month = MonthBox.SelectedIndex + 1;
        _monthChosenByUser = true;
    }

    private void YearBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updating || double.IsNaN(sender.Value)) return;
        _year = (int)Math.Clamp(sender.Value, 1950, 2100);
        _monthChosenByUser = true;
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
        if (AskForMonth) await AppServices.Store.SetFirstMonthLabelAsync(MonthLabels.For(new DateOnly(_year, _month, 1)));
        var saved = await ViewModel.AddAsync(MonthChoice.SelectedDayOrder);
        Error.Text = saved ? "" : ViewModel.Message;
        if (!saved) ViewModel.Message = "";
        args.Cancel = !saved;
        deferral.Complete();
    }
}
