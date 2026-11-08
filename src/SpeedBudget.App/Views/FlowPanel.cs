using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace SpeedBudget.App.Views;

/// <summary>
/// Lays its children out left to right and moves any that no longer fit onto a new line, so nothing is cut off when the window
/// is made narrow. Within a line, a child set to align Right sits at the line's right end and one set to Center is centred in the
/// line (as far as its neighbours allow); the rest follow each other from the left. When everything fits on one line this looks
/// like a row with items at the left, middle and right; as the window narrows the items drop to further lines.
/// </summary>
public sealed partial class FlowPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(FlowPanel), new PropertyMetadata(0.0, (d, _) => ((FlowPanel)d).InvalidateMeasure()));

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(FlowPanel), new PropertyMetadata(0.0, (d, _) => ((FlowPanel)d).InvalidateMeasure()));

    public double HorizontalSpacing { get => (double)GetValue(HorizontalSpacingProperty); set => SetValue(HorizontalSpacingProperty, value); }
    public double VerticalSpacing { get => (double)GetValue(VerticalSpacingProperty); set => SetValue(VerticalSpacingProperty, value); }

    private sealed class Line
    {
        public readonly List<(UIElement Child, Size Size)> Items = [];
        public double Width;
        public double Height;
    }

    private readonly List<Line> _lines = [];

    protected override Size MeasureOverride(Size available)
    {
        _lines.Clear();
        var line = new Line();
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;

            // Measured without a limit first, to know how wide it would like to be; only when it can't have that on a line of its own is it squeezed.
            child.Measure(new Size(double.PositiveInfinity, available.Height));
            var size = child.DesiredSize;
            if (size.Width > available.Width)
            {
                child.Measure(new Size(available.Width, available.Height));
                size = child.DesiredSize;
            }

            var needed = line.Items.Count == 0 ? size.Width : line.Width + HorizontalSpacing + size.Width;
            if (line.Items.Count > 0 && needed > available.Width + 0.5)
            {
                _lines.Add(line);
                line = new Line();
                needed = size.Width;
            }
            line.Items.Add((child, size));
            line.Width = needed;
            line.Height = Math.Max(line.Height, size.Height);
        }
        if (line.Items.Count > 0) _lines.Add(line);

        var width = _lines.Count == 0 ? 0 : _lines.Max(l => l.Width);
        var height = _lines.Sum(l => l.Height) + Math.Max(0, _lines.Count - 1) * VerticalSpacing;
        return new Size(double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width), height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var y = 0.0;
        foreach (var line in _lines)
        {
            var left = line.Items.Where(i => Place(i.Child) == Placement.Left).ToList();
            var right = line.Items.Where(i => Place(i.Child) == Placement.Right).ToList();
            var center = line.Items.Where(i => Place(i.Child) == Placement.Center).ToList();

            // Left items from the left edge; right items against the right edge.
            var x = 0.0;
            foreach (var (child, size) in left) { Put(child, x, y, size, line.Height); x += size.Width + HorizontalSpacing; }
            var leftEnd = x;

            var rx = final.Width;
            foreach (var (child, size) in Enumerable.Reverse(right)) { rx -= size.Width; Put(child, rx, y, size, line.Height); rx -= HorizontalSpacing; }
            var rightStart = rx;

            // Centred items are centred across the whole line when there is room between what is on either side of them.
            var centerWidth = center.Sum(c => c.Size.Width) + Math.Max(0, center.Count - 1) * HorizontalSpacing;
            var cx = (final.Width - centerWidth) / 2;
            cx = Math.Max(leftEnd, Math.Min(cx, rightStart - centerWidth));
            foreach (var (child, size) in center) { Put(child, cx, y, size, line.Height); cx += size.Width + HorizontalSpacing; }

            y += line.Height + VerticalSpacing;
        }
        return final;
    }

    private enum Placement { Left, Center, Right }

    private static Placement Place(UIElement child) =>
        (child as FrameworkElement)?.HorizontalAlignment switch
        {
            HorizontalAlignment.Right => Placement.Right,
            HorizontalAlignment.Center => Placement.Center,
            _ => Placement.Left,
        };

    private static void Put(UIElement child, double x, double y, Size size, double lineHeight)
    {
        var top = (child as FrameworkElement)?.VerticalAlignment switch
        {
            VerticalAlignment.Bottom => y + lineHeight - size.Height,
            VerticalAlignment.Top => y,
            _ => y + (lineHeight - size.Height) / 2,
        };
        child.Arrange(new Rect(x, top, size.Width, size.Height));
    }
}
