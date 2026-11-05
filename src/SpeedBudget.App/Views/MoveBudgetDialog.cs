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
    // Type to narrow the list down, or pick from it.
    private readonly AutoSuggestBox _item = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Type to search, or choose from the list",
        DisplayMemberPath = nameof(BudgetChoice.Display), TextMemberPath = nameof(BudgetChoice.Label),
    };
    private IReadOnlyList<BudgetChoice> _all = [];
    private BudgetChoice? _chosen;
    private readonly NumberBox _amount = new() { Header = "Amount (R)", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden };
    private readonly TextBlock _limit = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    private readonly Func<BudgetChoice, long> _maxFor;
    private readonly long _suggested;

    public BudgetChoice? Chosen => _chosen;

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
        _all = choices;
        _item.ItemsSource = choices;
        Money.Apply(_amount);

        // The list opens when the box is clicked or Down is pressed, not when the dialog appears.
        void ShowAll()
        {
            if (_chosen is not null || _item.Text.Trim() != "") return;
            _item.ItemsSource = _all;
            _item.IsSuggestionListOpen = true;
        }
        _item.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => ShowAll()), true);
        _item.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Down) ShowAll();
        }), true);
        _item.TextChanged += (s, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            _chosen = null;
            var text = s.Text.Trim();
            _item.ItemsSource = text == "" ? _all : _all.Where(c => c.Label.Contains(text, StringComparison.CurrentCultureIgnoreCase)).ToList();
            Validate();
        };
        _item.SuggestionChosen += (_, e) => { _chosen = e.SelectedItem as BudgetChoice; ChoiceChanged(); };
        // Enter on what was typed picks the first match.
        _item.QuerySubmitted += (_, e) =>
        {
            var pick = e.ChosenSuggestion as BudgetChoice ?? (_item.ItemsSource as IEnumerable<BudgetChoice>)?.FirstOrDefault();
            if (pick is null) return;
            _chosen = pick;
            _item.Text = pick.Label;
            ChoiceChanged();
        };
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
