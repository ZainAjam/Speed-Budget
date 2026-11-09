using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SpeedBudget.App.Views;

/// <summary>
/// Holds one table and zooms it: the table is laid out as if the room were bigger or smaller by the zoom, then scaled to fit
/// the room it really has. Everything outside a ZoomBox (headings, buttons, panels, pop-ups) stays at its normal size.
/// </summary>
public sealed partial class ZoomBox : Panel
{
    private readonly ScaleTransform _scale = new();

    public static readonly DependencyProperty AreaProperty =
        DependencyProperty.Register(nameof(Area), typeof(ZoomArea), typeof(ZoomBox), new PropertyMetadata(ZoomArea.Transactions, (d, _) => ((ZoomBox)d).InvalidateMeasure()));

    /// <summary>Which screen's zoom this table follows.</summary>
    public ZoomArea Area { get => (ZoomArea)GetValue(AreaProperty); set => SetValue(AreaProperty, value); }

    public static readonly DependencyProperty NaturalWidthProperty =
        DependencyProperty.Register(nameof(NaturalWidth), typeof(double), typeof(ZoomBox), new PropertyMetadata(0.0, (d, _) => ((ZoomBox)d).InvalidateMeasure()));

    /// <summary>
    /// The width the table needs at full size (0 for none). When the room is narrower than that, the table is made smaller, just enough
    /// to fit, instead of scrolling sideways; it is never made smaller than <see cref="SmallestFit"/> of its size.
    /// </summary>
    public double NaturalWidth { get => (double)GetValue(NaturalWidthProperty); set => SetValue(NaturalWidthProperty, value); }

    private const double SmallestFit = 0.5;

    /// <summary>The zoom chosen for the table, made smaller if the room is too narrow for the table at that zoom.</summary>
    private double EffectiveZoom(double roomWidth)
    {
        var zoom = AppServices.ZoomOf(Area);
        if (NaturalWidth <= 0 || double.IsInfinity(roomWidth) || roomWidth >= NaturalWidth * zoom) return zoom;
        return Math.Max(zoom * SmallestFit, roomWidth / NaturalWidth);
    }

    public ZoomBox()
    {
        Loaded += (_, _) => { AppServices.ZoomChanged += OnZoomChanged; InvalidateMeasure(); };
        Unloaded += (_, _) => AppServices.ZoomChanged -= OnZoomChanged;
    }

    private void OnZoomChanged() => DispatcherQueue.TryEnqueue(() => { InvalidateMeasure(); InvalidateArrange(); });

    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
    {
        if (Children.Count == 0) return new Windows.Foundation.Size(0, 0);
        var child = Children[0];
        var zoom = EffectiveZoom(available.Width);
        // At 100% sizes snap to whole pixels as usual. Zoomed, the table's own sizes are left exact so rows and columns can be fitted to the
        // screen's pixels after scaling (rounding them first would round them to the wrong grid).
        child.UseLayoutRounding = Math.Abs(zoom - 1) < 0.0001;
        child.Measure(new Windows.Foundation.Size(available.Width / zoom, available.Height / zoom));
        return new Windows.Foundation.Size(child.DesiredSize.Width * zoom, child.DesiredSize.Height * zoom);
    }

    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size final)
    {
        if (Children.Count == 0) return final;
        var child = Children[0];
        var zoom = EffectiveZoom(final.Width);
        child.RenderTransform = _scale;
        child.RenderTransformOrigin = new Windows.Foundation.Point(0, 0);
        _scale.ScaleX = _scale.ScaleY = zoom;
        child.Arrange(new Windows.Foundation.Rect(0, 0, final.Width / zoom, final.Height / zoom));
        return final;
    }
}
