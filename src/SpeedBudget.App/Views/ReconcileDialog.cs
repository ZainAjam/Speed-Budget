using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

/// <summary>
/// Asks for the account's current bank balance and checks it against the cleared transactions. It can only be
/// confirmed when the two match, which locks every cleared transaction as reconciled.
/// </summary>
public sealed class ReconcileDialog : ContentDialog
{
    private readonly TransactionsViewModel _vm;
    private readonly long _cleared;
    private readonly NumberBox _bank = new() { Header = "Current bank balance (R)", PlaceholderText = "0.00", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

    public ReconcileDialog(TransactionsViewModel vm, long clearedBalance, long? lastBankBalance)
    {
        _vm = vm;
        _cleared = clearedBalance;
        Title = $"Reconcile {vm.PageTitle}";
        PrimaryButtonText = "Reconcile";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        PrimaryButtonClick += Reconcile_Click;

        Money.Apply(_bank);
        _bank.Value = lastBankBalance is { } last ? (double)AppServices.ToRands(last) : double.NaN;
        _bank.ValueChanged += (_, _) => Update();

        Content = new StackPanel
        {
            Spacing = 12, MinWidth = 400,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, Opacity = 0.8,
                    Text = "Enter the balance your bank shows now. Tick Clear on every transaction the bank has processed first.",
                },
                _bank,
                new TextBlock { Text = $"Cleared balance in your records: {Format.Money(clearedBalance)}", Opacity = 0.8 },
                _status,
            },
        };
        Update();
    }

    private void Update()
    {
        if (double.IsNaN(_bank.Value))
        {
            _status.Text = "";
            IsPrimaryButtonEnabled = false;
            return;
        }
        var difference = AppServices.ToCents(_bank.Value) - _cleared;
        IsPrimaryButtonEnabled = difference == 0;
        _status.Text = difference == 0
            ? "The balances match. Reconciling locks the cleared transactions."
            : $"The bank balance is {Format.Money(Math.Abs(difference))} {(difference > 0 ? "more" : "less")} than your cleared transactions. " +
              "Tick the transactions that have cleared, or check the balance.";
    }

    private async void Reconcile_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Stay open to show the result, then offer Done.
        var deferral = args.GetDeferral();
        var (message, reconciled) = await _vm.ReconcileAsync(AppServices.ToCents(_bank.Value));
        _status.Text = message;
        if (reconciled)
        {
            PrimaryButtonText = "";
            CloseButtonText = "Done";
            _bank.IsEnabled = false;
        }
        args.Cancel = true;
        deferral.Complete();
    }
}
