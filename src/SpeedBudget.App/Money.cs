using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Globalization.NumberFormatting;

namespace SpeedBudget.App;

/// <summary>
/// Shows a NumberBox's amount as money: two decimals and thousands separators. Use
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

    public static void Apply(NumberBox box) =>
        box.NumberFormatter = new DecimalFormatter { FractionDigits = 2, IntegerDigits = 1, IsGrouped = true };
}
