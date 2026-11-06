using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SpeedBudget.App.Views;

/// <summary>A column heading with a small question mark beside it; hovering the question mark explains what the column is.</summary>
public sealed class ColumnTitle : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ColumnTitle), new PropertyMetadata("", (d, e) => ((ColumnTitle)d)._title.Text = (string)e.NewValue));

    public static readonly DependencyProperty HelpProperty =
        DependencyProperty.Register(nameof(Help), typeof(string), typeof(ColumnTitle), new PropertyMetadata("", (d, e) => ToolTipService.SetToolTip(((ColumnTitle)d)._help, (string)e.NewValue)));

    private readonly TextBlock _title = new()
    {
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
    };

    // The border gives the small icon a bigger target to hover over.
    private readonly Border _help = new()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Padding = new Thickness(3, 2, 3, 2), VerticalAlignment = VerticalAlignment.Center,
        Child = new FontIcon { Glyph = "\uE9CE", FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), Opacity = 0.8 },
    };

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Help { get => (string)GetValue(HelpProperty); set => SetValue(HelpProperty, value); }

    public ColumnTitle()
    {
        IsTabStop = false;
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { _title, _help } };
    }
}
