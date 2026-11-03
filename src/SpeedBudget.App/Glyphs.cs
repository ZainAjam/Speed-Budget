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
}
