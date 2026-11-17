using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace SpeedBudget.App.Views;

/// <summary>How the screens adapt to the width of the window (for example when it is snapped to half of a small screen).</summary>
public sealed partial class Responsive : ObservableObject
{
    /// <summary>Below this page width the screens switch to their compact layouts.</summary>
    public const double NarrowBelow = 900;

    public static bool IsNarrow(double pageWidth) => pageWidth > 0 && pageWidth < NarrowBelow;

    /// <summary>The page margin: roomy normally, slim in a narrow window so the content gets the room.</summary>
    public static Thickness PageMargin(double pageWidth) => IsNarrow(pageWidth) ? new Thickness(12, 14, 12, 10) : new Thickness(32, 24, 32, 24);

    /// <summary>The one shared state, which layouts can bind to (it follows the width of the area the pages are shown in).</summary>
    public static Responsive Current { get; } = new();

    [ObservableProperty] public partial bool Narrow { get; set; }
    public bool NotNarrow => !Narrow;
    partial void OnNarrowChanged(bool value) => OnPropertyChanged(nameof(NotNarrow));

    /// <summary>Called with the width of the area the pages are shown in.</summary>
    public void SetWidth(double width)
    {
        if (width > 0) Narrow = IsNarrow(width);
    }
}
