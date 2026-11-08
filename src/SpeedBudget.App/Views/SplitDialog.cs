using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;
using SpeedBudget.Data;

namespace SpeedBudget.App.Views;

/// <summary>
/// Spreads one transaction over several budget items. Amounts are typed as positive money in the direction
/// of the transaction; the lines must add up to its total before they can be saved.
/// </summary>
public sealed class SplitDialog : ContentDialog
{
    private readonly TransactionsViewModel _vm;
    private readonly Func<IReadOnlyList<(Guid? ItemId, long Amount, string? Memo)>, Task<string?>> _save;
    private readonly long _total;
    private int _sign;
    private readonly bool _openEnded;
    private readonly RadioButtons _direction = new() { Header = "This transaction is", MaxColumns = 2 };
    private readonly bool _requiresItem;
    private readonly StackPanel _lines = new() { Spacing = 8 };
    private readonly TextBlock _remaining = new();
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };
    private readonly List<(ItemPicker Item, NumberBox Amount, TextBox Memo, Grid Panel)> _entries = new();

    /// <summary>Splits a transaction that is already saved; the lines are saved straight to it.</summary>
    public static SplitDialog ForRow(TransactionsViewModel vm, TransactionRow row, IReadOnlyList<SplitLineEntity> existing) =>
        new(vm, $"{row.Payee}   ·   total {AppServices.ToRands(Math.Abs(row.Entity.Amount)):N2}", Math.Abs(row.Entity.Amount),
            row.Entity.Amount < 0 ? -1 : 1, vm.RequiresItem(row),
            existing.Select(l => (l.BudgetItemId, l.Amount, l.Memo)).ToList(), lines => vm.SaveSplitsAsync(row, lines));

    /// <param name="heading">The line above the split lines: who the transaction is with and its total.</param>
    /// <param name="total">The transaction's amount as a positive number of cents; the lines must add up to it.</param>
    /// <param name="sign">-1 when the money goes out, 1 when it comes in.</param>
    /// <param name="save">Takes the lines (signed) and returns an error sentence, or null when they were accepted.</param>
    /// <param name="openEnded">No total yet: the lines are typed first (with money out or in chosen here) and the total is their sum.</param>
    public SplitDialog(TransactionsViewModel vm, string heading, long total, int sign, bool requiresItem,
        IReadOnlyList<(Guid? ItemId, long Amount, string? Memo)> existing,
        Func<IReadOnlyList<(Guid? ItemId, long Amount, string? Memo)>, Task<string?>> save, bool openEnded = false)
    {
        _openEnded = openEnded;
        _vm = vm;
        _save = save;
        _sign = sign;
        _total = total;
        _requiresItem = requiresItem;

        Title = "Split transaction";
        Resources["ContentDialogMaxWidth"] = 720.0;
        PrimaryButtonText = "Save split";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        PrimaryButtonClick += Save_Click;

        var addLine = new Button { Content = "Add line" };
        addLine.Click += (_, _) => AddLine(null, double.NaN, null);

        var body = new StackPanel { Spacing = 12, MinWidth = 640 };
        body.Children.Add(new TextBlock { Text = heading, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        if (_openEnded)
        {
            _direction.Items.Add("Money out");
            _direction.Items.Add("Money in");
            _direction.SelectedIndex = sign > 0 ? 1 : 0;
            _direction.SelectionChanged += (_, _) => _sign = _direction.SelectedIndex == 1 ? 1 : -1;
            body.Children.Add(_direction);
        }
        body.Children.Add(_lines);
        body.Children.Add(addLine);
        body.Children.Add(_remaining);
        body.Children.Add(_error);
        Content = body;

        if (_openEnded)
        {
            if (existing.Count > 1) foreach (var line in existing) AddLine(line.ItemId, (double)AppServices.ToRands(Math.Abs(line.Amount)), line.Memo);
            else
            {
                AddLine(existing.FirstOrDefault().ItemId, double.NaN, null);
                AddLine(null, double.NaN, null);
            }
        }
        else if (existing.Count > 1)
            foreach (var line in existing) AddLine(line.ItemId, (double)AppServices.ToRands(Math.Abs(line.Amount)), line.Memo);
        else
        {
            AddLine(existing.FirstOrDefault().ItemId, (double)AppServices.ToRands(_total), null);
            AddLine(null, double.NaN, null);
        }
        UpdateRemaining();
    }

    private void AddLine(Guid? itemId, double amount, string? memo)
    {
        var item = new ItemPicker
        {
            ItemsSource = _vm.ItemOptions, Width = 250, AllowCreate = false,
            SelectedItem = _vm.ItemOptions.FirstOrDefault(o => o.Id == itemId) ?? _vm.NoItem,
        };
        var amountBox = new NumberBox { Value = amount, Width = 110, PlaceholderText = "0.00", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden };
        Money.Apply(amountBox);
        var memoBox = new TextBox { Text = memo ?? "", PlaceholderText = "Memo", Width = 130 };
        var remove = new Button { Content = "✕" };
        ToolTipService.SetToolTip(remove, "Remove this line");

        var panel = new Grid { ColumnSpacing = 8 };
        foreach (var width in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto })
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        Grid.SetColumn(amountBox, 1);
        Grid.SetColumn(memoBox, 2);
        Grid.SetColumn(remove, 3);
        panel.Children.Add(item);
        panel.Children.Add(amountBox);
        panel.Children.Add(memoBox);
        panel.Children.Add(remove);

        var entry = (item, amountBox, memoBox, panel);
        _entries.Add(entry);
        _lines.Children.Add(panel);
        amountBox.ValueChanged += (_, _) => UpdateRemaining();
        remove.Click += (_, _) =>
        {
            if (_entries.Count <= 1) return;
            _entries.Remove(entry);
            _lines.Children.Remove(panel);
            UpdateRemaining();
        };
    }

    private long Cents(NumberBox box) => AppServices.ToCents(box.Value);

    private void UpdateRemaining()
    {
        if (_openEnded)
        {
            var sum = _entries.Sum(e => Cents(e.Amount));
            _remaining.Text = $"Total: {AppServices.ToRands(sum):N2}";
            _remaining.Opacity = 1;
            IsPrimaryButtonEnabled = sum > 0;
            return;
        }
        var left = _total - _entries.Sum(e => Cents(e.Amount));
        _remaining.Text = left == 0 ? "The lines add up to the total." : $"Left to assign: {AppServices.ToRands(left):N2}";
        _remaining.Opacity = left == 0 ? 0.7 : 1;
        IsPrimaryButtonEnabled = left == 0;
    }

    private async void Save_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Hold the dialog open until the store has accepted the lines.
        var deferral = args.GetDeferral();
        string? error = null;
        var lines = new List<(Guid? ItemId, long Amount, string? Memo)>();
        foreach (var (item, amount, memo, _) in _entries)
        {
            var cents = Cents(amount);
            var itemId = item.SelectedItem?.Id;
            if (cents <= 0) { error = "Every line needs an amount above zero. Remove lines you don't need."; break; }
            if (_requiresItem && itemId is null) { error = "Choose a budget item for every line."; break; }
            lines.Add((itemId, cents * _sign, string.IsNullOrWhiteSpace(memo.Text) ? null : memo.Text.Trim()));
        }
        error ??= await _save(lines);
        _error.Text = error ?? "";
        args.Cancel = error is not null;
        deferral.Complete();
    }
}
