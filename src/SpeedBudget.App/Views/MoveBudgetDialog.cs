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
    // A drop-down with a search box inside it, and what is left on each item shown on the right (green when there is money left).
    private readonly ItemPicker _item = new() { AllowCreate = false, ValueOverride = "Choose a budget item" };
    private readonly Dictionary<Guid, BudgetChoice> _byId = [];
    private BudgetChoice? _chosen;
    private readonly NumberBox _amount = new() { Header = "Amount (R)", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden };
    private readonly TextBlock _limit = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    private readonly Func<BudgetChoice, long> _maxFor;
    private readonly long _suggested;

    public BudgetChoice? Chosen => _chosen;

    public long AmountCents => AppServices.ToCents(_amount.Value);

    /// <param name="maxFor">The most that may be moved when the given item is the other side.</param>
    /// <param name="suggested">The amount filled in first (capped to what the chosen item allows).</param>
    /// <param name="headline">The first thing the dialog says, on a line of its own, in bold and colour: how much is available, left or short.</param>
    /// <param name="shortfall">Whether the headline is about money that is missing (red) rather than money there is (green).</param>
    /// <param name="intro">What to do about it.</param>
    public MoveBudgetDialog(string title, string headline, string intro, string itemHeader, string primaryText,
        IReadOnlyList<BudgetChoice> choices, Func<BudgetChoice, long> maxFor, long suggested, bool shortfall = false)
    {
        _maxFor = maxFor;
        _suggested = suggested;
        Title = title;
        PrimaryButtonText = primaryText;
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        IsPrimaryButtonEnabled = false;

        _item.Header = itemHeader;
        foreach (var c in choices) _byId[c.ItemId] = c;
        _item.ItemsSource = choices.Select(c => new ItemOption(c.ItemId, c.Label, null, null, c.Label, c.Label) { Balance = c.LeftCents }).ToList();
        _item.RegisterPropertyChangedCallback(ItemPicker.SelectedItemProperty, (_, _) =>
        {
            _chosen = _item.SelectedItem?.Id is { } id && _byId.TryGetValue(id, out var picked) ? picked : null;
            _item.ValueOverride = "";
            ChoiceChanged();
        });
        Money.Apply(_amount);
        _amount.ValueChanged += (_, _) => Validate();

        var body = new StackPanel
        {
            Spacing = 12, MinWidth = 420,
            Children =
            {
                new TextBlock
                {
                    Text = headline, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = Format.Theme(shortfall ? "SystemFillColorCriticalBrush" : "SystemFillColorSuccessBrush"),
                },
                new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, -6, 0, 0) },
                _item, _amount, _limit,
            },
        };
        Content = body;
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
