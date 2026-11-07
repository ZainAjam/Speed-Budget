using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SpeedBudget.App.Views;

/// <summary>A column heading; hovering the title explains what the column is. A small funnel beside it opens the column's filter.</summary>
public sealed class ColumnTitle : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ColumnTitle), new PropertyMetadata("", (d, e) => ((ColumnTitle)d)._title.Text = (string)e.NewValue));

    public static readonly DependencyProperty HelpProperty =
        DependencyProperty.Register(nameof(Help), typeof(string), typeof(ColumnTitle), new PropertyMetadata("", (d, e) => ToolTipService.SetToolTip(((ColumnTitle)d)._title, (string)e.NewValue)));

    private readonly TextBlock _title = new()
    {
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
    };

    // A small funnel after the title, as in Excel: pressing it opens the column's filter. It is gold while the column is filtered.
    private readonly FontIcon _funnel = new() { Glyph = "", FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), Opacity = 0.8 };

    private readonly Border _filter = new()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Padding = new Thickness(3, 2, 3, 2), VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed,
    };

    public static readonly DependencyProperty FilterKeyProperty =
        DependencyProperty.Register(nameof(FilterKey), typeof(string), typeof(ColumnTitle), new PropertyMetadata("", (d, e) =>
            ((ColumnTitle)d)._filter.Visibility = (string)e.NewValue == "" ? Visibility.Collapsed : Visibility.Visible));

    /// <summary>The column this heading filters. Empty for a heading with no filter.</summary>
    public string FilterKey { get => (string)GetValue(FilterKeyProperty); set => SetValue(FilterKeyProperty, value); }

    private bool _isFiltered;

    public bool IsFiltered
    {
        get => _isFiltered;
        set
        {
            _isFiltered = value;
            _funnel.Foreground = new SolidColorBrush(value ? Microsoft.UI.ColorHelper.FromArgb(255, 255, 213, 79) : Microsoft.UI.Colors.White);
            _funnel.Opacity = value ? 1 : 0.8;
            ToolTipService.SetToolTip(_filter, value ? "Filtered. Press to change or clear the filter." : "Filter this column");
        }
    }

    /// <summary>Raised when the funnel is pressed.</summary>
    public event Action<ColumnTitle>? FilterClicked;

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Help { get => (string)GetValue(HelpProperty); set => SetValue(HelpProperty, value); }

    public ColumnTitle()
    {
        IsTabStop = false;
        _filter.Child = _funnel;
        ToolTipService.SetToolTip(_filter, "Filter this column");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_filter, "Filter this column");
        _filter.Tapped += (_, e) => { e.Handled = true; FilterClicked?.Invoke(this); };
        _filter.PointerPressed += (_, e) => e.Handled = true;   // pressing the funnel must not start anything the heading does (such as a drag)
        // The title gives way (and is trimmed) in a narrow column, so the funnel is never cut off.
        var row = new Grid { ColumnSpacing = 2 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_filter, 1);
        row.Children.Add(_title);
        row.Children.Add(_filter);
        Content = row;
    }
}
