using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

/// <summary>
/// Asks how much of an income line to hold back for next month. The full amount still available is filled in,
/// so holding everything is one click.
/// </summary>
public sealed class HoldDialog : ContentDialog
{
    private readonly NumberBox _amount = new() { Header = "Amount to hold for next month (R)", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden };
    private readonly long _available;

    public long AmountCents => AppServices.ToCents(_amount.Value);

    public HoldDialog(IncomeCell cell)
    {
        _available = cell.AvailableCents;
        Title = $"Hold {cell.Name} for next month";
        PrimaryButtonText = "Hold";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        Money.Apply(_amount);
        _amount.Value = (double)AppServices.ToRands(_available);
        _amount.ValueChanged += (_, _) => IsPrimaryButtonEnabled = AmountCents > 0 && AmountCents <= _available;

        Content = new StackPanel
        {
            Spacing = 12, MinWidth = 360,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, Opacity = 0.8,
                    Text = $"{Format.Money(_available)} is available now. Whatever you hold stays out of this month's budget and appears as released income next month.",
                },
                _amount,
            },
        };
        Opened += (_, _) => _amount.Focus(FocusState.Programmatic);
    }
}
