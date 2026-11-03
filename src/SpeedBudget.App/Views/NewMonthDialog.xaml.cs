using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

public sealed partial class NewMonthDialog : ContentDialog
{
    private readonly TransactionsViewModel _vm;
    private string _suggested = "";

    public string MonthName => LabelBox.Text.Trim();
    public DateTimeOffset StartDate => DateBox.Date ?? DateTimeOffset.Now;
    public bool CopyBudget => CopyBox.IsChecked == true;

    public NewMonthDialog(TransactionsViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DateBox.Date = DateTimeOffset.Now;
        Suggest();
    }

    /// <summary>Follows the date with a suggested name, until the user types their own.</summary>
    private void Suggest()
    {
        var next = _vm.SuggestMonthLabel(StartDate);
        if (LabelBox.Text == "" || LabelBox.Text == _suggested) LabelBox.Text = next;
        _suggested = next;
    }

    private void DateBox_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => Suggest();

    private void LabelBox_TextChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(LabelBox.Text);
}
