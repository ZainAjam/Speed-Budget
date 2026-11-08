using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace SpeedBudget.App.Views;

/// <summary>
/// Holds one row of items. When the room is wide enough the row is laid out as usual, stretched across it. When the room is too
/// narrow for the row at its natural size, the whole row is made smaller (text, buttons and all) just enough to fit, so every
/// item stays where it was and nothing is cut off.
/// </summary>
public sealed partial class ShrinkBox : Panel
{
    /// <summary>It is never made smaller than this, so the text stays readable.</summary>
    private const double SmallestScale = 0.55;

    private readonly ScaleTransform _scale = new();
    private double _natural;      // the width the row wants at full size
    private double _scaleNow = 1;

    protected override Size MeasureOverride(Size available)
    {
        if (Children.Count == 0) return new Size(0, 0);
        var child = Children[0];

        child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _natural = child.DesiredSize.Width;
        var room = double.IsInfinity(available.Width) ? _natural : available.Width;

        if (_natural <= room || _natural <= 0)
        {
            _scaleNow = 1;
            child.Measure(new Size(room, double.PositiveInfinity));   // fills the room, so items meant for the far side stay on it
            return new Size(room, child.DesiredSize.Height);
        }

        _scaleNow = Math.Max(SmallestScale, room / _natural);
        return new Size(room, child.DesiredSize.Height * _scaleNow);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (Children.Count == 0) return final;
        var child = Children[0];
        child.RenderTransform = _scale;
        child.RenderTransformOrigin = new Point(0, 0);
        _scale.ScaleX = _scale.ScaleY = _scaleNow;

        if (_scaleNow >= 1)
            child.Arrange(new Rect(0, 0, final.Width, child.DesiredSize.Height));
        else
            child.Arrange(new Rect(0, 0, _natural, child.DesiredSize.Height));   // laid out at full size, then scaled down to fit
        return final;
    }
}
