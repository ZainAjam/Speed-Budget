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
    private readonly TransactionRow _row;
    private readonly long _total;
    private readonly int _sign;
    private readonly bool _requiresItem;
    private readonly StackPanel _lines = new() { Spacing = 8 };
    private readonly TextBlock _remaining = new();
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };
    private readonly List<(ComboBox Item, NumberBox Amount, TextBox Memo, Grid Panel)> _entries = new();

    public SplitDialog(TransactionsViewModel vm, TransactionRow row, IReadOnlyList<SplitLineEntity> existing)
    {
        _vm = vm;
        _row = row;
        _sign = row.Entity.Amount < 0 ? -1 : 1;
        _total = Math.Abs(row.Entity.Amount);
        _requiresItem = vm.RequiresItem(row);

        Title = "Split transaction";
        PrimaryButtonText = "Save split";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        PrimaryButtonClick += Save_Click;

        var addLine = new Button { Content = "Add line" };
        addLine.Click += (_, _) => AddLine(null, double.NaN, null);

        Content = new StackPanel
        {
            Spacing = 12,
            MinWidth = 560,
            Children =
            {
                new TextBlock { Text = $"{row.Payee}   ·   total {AppServices.ToRands(_total):N2}", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] },
                _lines,
                addLine,
                _remaining,
                _error,
            },
        };

        if (existing.Count > 1)
            foreach (var line in existing) AddLine(line.BudgetItemId, (double)AppServices.ToRands(Math.Abs(line.Amount)), line.Memo);
        else
        {
            AddLine(existing.FirstOrDefault()?.BudgetItemId, (double)AppServices.ToRands(_total), null);
            AddLine(null, double.NaN, null);
        }
        UpdateRemaining();
    }

    private void AddLine(Guid? itemId, double amount, string? memo)
    {
        var item = new ComboBox
        {
            ItemsSource = _vm.ItemOptions, DisplayMemberPath = "Name", Width = 260, PlaceholderText = "Budget item",
            SelectedItem = _vm.ItemOptions.FirstOrDefault(o => o.Id == itemId) ?? _vm.NoItem,
        };
        var amountBox = new NumberBox { Value = amount, Width = 120, PlaceholderText = "0.00", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden };
        Money.Apply(amountBox);
        var memoBox = new TextBox { Text = memo ?? "", PlaceholderText = "Memo", Width = 140 };
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
            var itemId = (item.SelectedItem as ItemOption)?.Id;
            if (cents <= 0) { error = "Every line needs an amount above zero. Remove lines you don't need."; break; }
            if (_requiresItem && itemId is null) { error = "Choose a budget item for every line."; break; }
            lines.Add((itemId, cents * _sign, string.IsNullOrWhiteSpace(memo.Text) ? null : memo.Text.Trim()));
        }
        error ??= await _vm.SaveSplitsAsync(_row, lines);
        _error.Text = error ?? "";
        args.Cancel = error is not null;
        deferral.Complete();
    }
}
