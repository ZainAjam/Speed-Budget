using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SpeedBudget.App.ViewModels;

internal static class Format
{
    public static string Money(long cents) => AppServices.ToRands(cents).ToString("N2");

    public static Brush Theme(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>Green when positive, red when negative, and the normal text colour at zero.</summary>
    public static Brush Signed(long cents) => Theme(cents > 0 ? "SystemFillColorSuccessBrush"
        : cents < 0 ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush");
}
