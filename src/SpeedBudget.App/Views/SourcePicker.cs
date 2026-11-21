using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.Data;

namespace SpeedBudget.App.Views;

/// <summary>
/// The boxes that say where one figure of a bank file is: how to find it (not in the file, the last line of a column, or one cell), which
/// column, and which row. For the file's date the choices are the date of its latest line, or one cell. A small note at the end shows what that
/// reads as, so the user can see at once whether the place is right.
/// </summary>
public sealed class SourcePicker
{
    private readonly bool _isDate;
    private bool _loading;

    public ComboBox Kind { get; }
    public ComboBox Column { get; } = new() { Width = 230, DisplayMemberPath = nameof(ColumnChoice.Label) };
    public NumberBox Row { get; } = new() { Minimum = 1, Maximum = 100000, Value = 1, Width = 150, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
    public TextBlock Shown { get; } = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
    public StackPanel Panel { get; }

    private readonly TextBlock _columnLabel = new() { Text = "column", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
    private readonly TextBlock _rowLabel = new() { Text = "row", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };

    /// <summary>Raised when the user changes any of the boxes.</summary>
    public event Action? Changed;

    public SourcePicker(bool isDate)
    {
        _isDate = isDate;
        Kind = new ComboBox { Width = 210, ItemsSource = isDate ? new[] { "Date of the last line", "A cell in the file" } : new[] { "Not in the file", "Last line of a column", "A cell in the file" }, SelectedIndex = 0 };
        Panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Kind, _columnLabel, Column, _rowLabel, Row, Shown } };
        Kind.SelectionChanged += (_, _) => { ShowParts(); Raise(); };
        Column.SelectionChanged += (_, _) => Raise();
        Row.ValueChanged += (_, _) => Raise();
        ShowParts();
    }

    private void Raise() { if (!_loading) Changed?.Invoke(); }

    private int KindOf(int index) => _isDate ? (index == 1 ? 2 : 0) : index;

    private void ShowParts()
    {
        var kind = KindOf(Kind.SelectedIndex);
        var showColumn = kind != 0;
        _columnLabel.Visibility = Column.Visibility = showColumn ? Visibility.Visible : Visibility.Collapsed;
        _rowLabel.Visibility = Row.Visibility = kind == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Offers the columns of the file to choose from.</summary>
    public void SetColumns(IReadOnlyList<ColumnChoice> columns)
    {
        var keep = (Column.SelectedItem as ColumnChoice)?.Index ?? -1;
        _loading = true;
        Column.ItemsSource = columns;
        Column.SelectedItem = columns.FirstOrDefault(c => c.Index == keep) ?? columns.FirstOrDefault();
        _loading = false;
    }

    /// <summary>The place the boxes point at.</summary>
    public BalanceSource Value
    {
        get
        {
            var kind = KindOf(Kind.SelectedIndex);
            if (kind == 0) return BalanceSource.None;
            return new BalanceSource(kind, (Column.SelectedItem as ColumnChoice)?.Index ?? -1, kind == 2 ? (int)(double.IsNaN(Row.Value) ? 1 : Row.Value) : 0);
        }
        set
        {
            _loading = true;
            Kind.SelectedIndex = _isDate ? (value.Kind == 2 ? 1 : 0) : Math.Clamp(value.Kind, 0, 2);
            if (value.Column >= 0 && Column.ItemsSource is IEnumerable<ColumnChoice> columns && columns.FirstOrDefault(c => c.Index == value.Column) is { } match) Column.SelectedItem = match;
            Row.Value = value.Row >= 1 ? value.Row : 1;
            ShowParts();
            _loading = false;
        }
    }
}
