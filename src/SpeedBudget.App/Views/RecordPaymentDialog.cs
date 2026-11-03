using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

/// <summary>Adds the transaction for a payment: an outflow on the item, from one of the budget accounts.</summary>
public sealed class RecordPaymentDialog : ContentDialog
{
    private readonly PaymentsViewModel _vm;
    private readonly PaymentRow _row;
    private readonly ComboBox _account = new() { Header = "Paid from", HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = "Name" };
    private readonly CalendarDatePicker _date = new() { Header = "Date", Date = DateTimeOffset.Now };
    private readonly NumberBox _amount = new() { Header = "Amount (R)", PlaceholderText = "0.00", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden };
    private readonly TextBox _payee = new() { Header = "Payee" };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };

    public RecordPaymentDialog(PaymentsViewModel vm, PaymentRow row)
    {
        _vm = vm;
        _row = row;
        Title = $"Record payment: {row.Name}";
        PrimaryButtonText = "Record";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        PrimaryButtonClick += Record_Click;

        _account.ItemsSource = vm.BudgetAccounts;
        if (vm.BudgetAccounts.Count > 0) _account.SelectedIndex = 0;
        _amount.Value = row.ToPayCents > 0 ? (double)AppServices.ToRands(row.ToPayCents) : double.NaN;
        _payee.Text = row.Name;

        Content = new StackPanel
        {
            Spacing = 12, MinWidth = 380,
            Children = { _account, _date, _amount, _payee, _error },
        };
    }

    private async void Record_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Hold the dialog open until the store has accepted the payment.
        var deferral = args.GetDeferral();
        var error = await _vm.RecordPaymentAsync(_row, (_account.SelectedItem as AccountOption)?.Id,
            _date.Date ?? DateTimeOffset.Now, _amount.Value, _payee.Text);
        _error.Text = error ?? "";
        args.Cancel = error is not null;
        deferral.Complete();
    }
}
