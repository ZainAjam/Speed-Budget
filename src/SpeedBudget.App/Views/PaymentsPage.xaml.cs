using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

public sealed partial class PaymentsPage : Page
{
    public PaymentsViewModel ViewModel { get; } = new();

    public PaymentsPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PaymentsViewModel.HasPayments))
                EmptyHint.Visibility = ViewModel.HasPayments ? Visibility.Collapsed : Visibility.Visible;
        };
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ViewModel.GoBack();

    private void Forward_Click(object sender, RoutedEventArgs e) => ViewModel.GoForward();

    private async void Record_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PaymentRow row) return;
        var dialog = new RecordPaymentDialog(ViewModel, row) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }
}
