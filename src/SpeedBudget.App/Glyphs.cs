using Microsoft.UI.Text;
using Windows.UI.Text;

namespace SpeedBudget.App;

/// <summary>Icon glyphs and styling chosen from data, for use in function bindings.</summary>
public static class Glyphs
{
    /// <summary>Down when a group is open, right when it is folded away.</summary>
    public static string Chevron(bool expanded) => expanded ? "" : "";

    public static FontWeight Weight(bool strong) => strong ? FontWeights.Bold : FontWeights.Normal;

    /// <summary>The summary block's emphasised row is taller and its figures larger.</summary>
    public static double SummaryRowHeight(bool strong) => strong ? 48 : 34;

    public static double SummarySize(bool strong) => strong ? 22 : 14;

    /// <summary>The icon for an account that counts toward the budget: a payment card. Used in the side panel and on the account's page.</summary>
    public static Microsoft.UI.Xaml.Controls.IconElement BudgetAccountIcon() => new Microsoft.UI.Xaml.Controls.FontIcon { Glyph = "\uE8C7" };

    /// <summary>
    /// The icon for an account that is only being tracked: a radar (two rings, a centre dot and a sweep line). It is drawn as a shape, in the
    /// text colour, so it is the same colour as the other icons. Used in the side panel and on the account's page.
    /// </summary>
    public static Microsoft.UI.Xaml.Controls.IconElement TrackingAccountIcon()
    {
        // Drawn on a 24 by 24 grid. The rings run opposite ways round, so that the inside of each is left open; the sweep line is filled.
        const string data =
            "F1 M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 Z " +          // outer ring, clockwise
            "M20.5,12 A8.5,8.5 0 1 0 3.5,12 A8.5,8.5 0 1 0 20.5,12 Z " +   // its inside, anticlockwise
            "M17.5,12 A5.5,5.5 0 1 1 6.5,12 A5.5,5.5 0 1 1 17.5,12 Z " +   // inner ring, clockwise
            "M16,12 A4,4 0 1 0 8,12 A4,4 0 1 0 16,12 Z " +                 // its inside, anticlockwise
            "M13.5,12 A1.5,1.5 0 1 1 10.5,12 A1.5,1.5 0 1 1 13.5,12 Z " +  // the centre dot
            "M11.3,12.7 L18.7,4.3 L19.7,5.3 L12.7,13.3 Z";                // the sweep line
        return new Microsoft.UI.Xaml.Controls.PathIcon { Data = (Microsoft.UI.Xaml.Media.Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Microsoft.UI.Xaml.Media.Geometry), data) };
    }
}
