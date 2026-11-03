using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SpeedBudget.App.ViewModels;

internal static class Format
{
    public static string Money(long cents) => AppServices.ToRands(cents).ToString("N2");

    public static Brush Theme(string key) => (Brush)Application.Current.Resources[key];

    private static readonly Brush ClearBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    /// <summary>A row with nothing behind it. It still receives clicks, unlike a missing background.</summary>
    public static Brush Clear => ClearBrush;

    /// <summary>A soft wash of the accent colour, to draw the eye to a row for a moment.</summary>
    public static Brush Highlight => new SolidColorBrush((Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]) { Opacity = 0.35 };

    /// <summary>Green when positive, red when negative, and the normal text colour at zero.</summary>
    public static Brush Signed(long cents) => Theme(cents > 0 ? "SystemFillColorSuccessBrush"
        : cents < 0 ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush");
}
