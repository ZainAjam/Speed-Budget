using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Globalization.NumberFormatting;

namespace SpeedBudget.App;

/// <summary>
/// Shows a NumberBox's amount as money: two decimals, thousands separators, right-aligned. Use
/// <c>app:Money.Format="True"</c> in XAML, or call <see cref="Apply"/> for boxes built in code.
/// </summary>
public static class Money
{
    public static readonly DependencyProperty FormatProperty =
        DependencyProperty.RegisterAttached("Format", typeof(bool), typeof(Money), new PropertyMetadata(false, OnFormatChanged));

    public static bool GetFormat(DependencyObject element) => (bool)element.GetValue(FormatProperty);

    public static void SetFormat(DependencyObject element, bool value) => element.SetValue(FormatProperty, value);

    private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NumberBox box && e.NewValue is true) Apply(box);
    }

    public static void Apply(NumberBox box)
    {
        box.NumberFormatter = new DecimalFormatter { FractionDigits = 2, IntegerDigits = 1, IsGrouped = true };
        box.Loaded -= RightAlign;
        box.Loaded += RightAlign;
    }

    // NumberBox has no alignment of its own, so reach the text box inside its template once it is loaded.
    private static void RightAlign(object sender, RoutedEventArgs e)
    {
        if (sender is NumberBox box && FindTextBox(box) is { } input) input.TextAlignment = Microsoft.UI.Xaml.TextAlignment.Right;
    }

    internal static TextBox? FindTextBox(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox box) return box;
            if (FindTextBox(child) is { } found) return found;
        }
        return null;
    }
}
