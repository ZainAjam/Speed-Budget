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

    /// <summary>Holds back some of one income line.</summary>
    public HoldDialog(IncomeCell cell)
        : this($"Hold {cell.Name} for next month", cell.AvailableCents,
               $"{Format.Money(cell.AvailableCents)} is available now. Whatever you hold stays out of this month's budget and appears as released income next month.")
    {
    }

    /// <summary>Holds back some of what is available to budget in a month (taken from its income lines).</summary>
    /// <param name="largest">The most that can be held.</param>
    public HoldDialog(string title, long largest, string explanation, string primaryText = "Hold", string header = "Amount to hold for next month (R)")
    {
        _available = largest;
        Title = title;
        PrimaryButtonText = primaryText;
        _amount.Header = header;
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
                    Text = explanation,
                },
                _amount,
            },
        };
        Opened += (_, _) => _amount.Focus(FocusState.Programmatic);
    }
}
