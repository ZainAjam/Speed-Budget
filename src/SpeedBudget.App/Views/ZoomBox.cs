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
        var zoom = AppServices.ZoomOf(Area);
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
        var zoom = AppServices.ZoomOf(Area);
        child.RenderTransform = _scale;
        child.RenderTransformOrigin = new Windows.Foundation.Point(0, 0);
        _scale.ScaleX = _scale.ScaleY = zoom;
        child.Arrange(new Windows.Foundation.Rect(0, 0, final.Width / zoom, final.Height / zoom));
        return final;
    }
}
