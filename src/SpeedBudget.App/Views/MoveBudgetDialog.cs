using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

/// <summary>A budget item offered when moving budget around, with what is left on it this month.</summary>
public sealed record BudgetChoice(Guid ItemId, string Label, long LeftCents)
{
    public string Display => $"{Label}    ({Format.Money(LeftCents)} left)";
}

/// <summary>
/// Asks which other budget item to move budget to (or take it from) and how much. The amount starts at the most that
/// makes sense and can't go above what the chosen item allows.
/// </summary>
public sealed class MoveBudgetDialog : ContentDialog
{
    private readonly ComboBox _item = new() { HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = nameof(BudgetChoice.Display) };
    private readonly NumberBox _amount = new() { Header = "Amount (R)", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden };
    private readonly TextBlock _limit = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    private readonly Func<BudgetChoice, long> _maxFor;
    private readonly long _suggested;

    public BudgetChoice? Chosen => _item.SelectedItem as BudgetChoice;

    public long AmountCents => AppServices.ToCents(_amount.Value);

    /// <param name="maxFor">The most that may be moved when the given item is the other side.</param>
    /// <param name="suggested">The amount filled in first (capped to what the chosen item allows).</param>
    public MoveBudgetDialog(string title, string intro, string itemHeader, string primaryText,
        IReadOnlyList<BudgetChoice> choices, Func<BudgetChoice, long> maxFor, long suggested)
    {
        _maxFor = maxFor;
        _suggested = suggested;
        Title = title;
        PrimaryButtonText = primaryText;
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        IsPrimaryButtonEnabled = false;

        _item.Header = itemHeader;
        _item.ItemsSource = choices;
        Money.Apply(_amount);

        _item.SelectionChanged += (_, _) => ChoiceChanged();
        _amount.ValueChanged += (_, _) => Validate();

        Content = new StackPanel
        {
            Spacing = 12, MinWidth = 420,
            Children = { new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 }, _item, _amount, _limit },
        };
        if (choices.Count == 0)
            _limit.Text = "There is no other budget item this can be used with.";
    }

    private void ChoiceChanged()
    {
        if (Chosen is not { } choice) return;
        var max = _maxFor(choice);
        _amount.Value = (double)AppServices.ToRands(Math.Min(_suggested, max));
        _limit.Text = $"At most {Format.Money(max)} can be moved with this item.";
        Validate();
    }

    private void Validate() =>
        IsPrimaryButtonEnabled = Chosen is { } choice && AmountCents > 0 && AmountCents <= _maxFor(choice);
}
