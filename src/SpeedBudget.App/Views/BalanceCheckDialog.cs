using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using SpeedBudget.App.ViewModels;
using Windows.UI;

namespace SpeedBudget.App.Views;

/// <summary>The result of comparing the balance a bank file ends on with the account's cleared balance in the app, as at the file's last date.</summary>
public sealed record BalanceCheck(DateOnly Date, long FileBalance, long AppBalance)
{
    public bool Matches => FileBalance == AppBalance;
}

/// <summary>
/// Shown after an import is applied when the file had balances: a big green tick that draws itself when the account agrees with the bank, or a
/// yellow warning triangle that bounces in and wobbles when it does not, with a message saying which.
/// </summary>
public sealed class BalanceCheckDialog : ContentDialog
{
    private static readonly Color Green = Color.FromArgb(255, 46, 160, 67);
    private static readonly Color Yellow = Color.FromArgb(255, 245, 180, 0);

    public BalanceCheckDialog(BalanceCheck check)
    {
        CloseButtonText = "OK";
        DefaultButton = ContentDialogButton.Close;
        Resources["ContentDialogMinWidth"] = 480.0;

        var picture = check.Matches ? TickPicture() : WarningPicture();
        var title = new TextBlock
        {
            Text = check.Matches ? "Everything reconciles" : "Not everything reconciles",
            FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
        };
        var detail = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 420, Opacity = 0.85,
            Text = check.Matches
                ? $"The bank's balance in the file ({Format.Money(check.FileBalance)} on {check.Date:d MMM yyyy}) is the same as the account's cleared balance in Speed Budget."
                : $"The file says the balance was {Format.Money(check.FileBalance)} on {check.Date:d MMM yyyy}, but the account's cleared balance in Speed Budget is {Format.Money(check.AppBalance)}: "
                  + $"{Format.Money(Math.Abs(check.FileBalance - check.AppBalance))} {(check.AppBalance > check.FileBalance ? "too much" : "too little")}. "
                  + "A transaction may be missing, entered wrongly, or not cleared yet.",
        };
        Content = new StackPanel { Spacing = 18, Padding = new Thickness(0, 12, 0, 4), HorizontalAlignment = HorizontalAlignment.Center, Children = { picture, title, detail } };
    }

    // ---- The green tick: the disc pops in, then the tick draws itself ----

    private static FrameworkElement TickPicture()
    {
        const double size = 150;
        var disc = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Green), RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5) };
        var discScale = new ScaleTransform { ScaleX = 0, ScaleY = 0 };
        disc.RenderTransform = discScale;

        var tick = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = TickGeometry(),
            Stroke = new SolidColorBrush(Colors.White), StrokeThickness = 12,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            StrokeDashArray = new DoubleCollection { 10, 10 },   // as long as the tick (in line widths): the offset slides it into view
            StrokeDashOffset = 10,
        };
        var host = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5) };
        var hostScale = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
        host.RenderTransform = hostScale;
        var canvas = new Canvas { Width = size, Height = size };
        canvas.Children.Add(tick);
        host.Children.Add(disc);
        host.Children.Add(canvas);

        var story = new Storyboard();
        Animate(story, discScale, "ScaleX", 0, 1, 0, 450, new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut });
        Animate(story, discScale, "ScaleY", 0, 1, 0, 450, new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut });
        Animate(story, tick, "StrokeDashOffset", 10, 0, 380, 520, new CubicEase { EasingMode = EasingMode.EaseOut });
        Animate(story, hostScale, "ScaleX", 1, 1.08, 950, 160, new SineEase { EasingMode = EasingMode.EaseOut }, autoReverse: true);   // a small pulse once it is drawn
        Animate(story, hostScale, "ScaleY", 1, 1.08, 950, 160, new SineEase { EasingMode = EasingMode.EaseOut }, autoReverse: true);
        host.Loaded += (_, _) => story.Begin();
        return host;
    }

    private static Geometry TickGeometry()
    {
        var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(42, 78) };
        figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(66, 102) });
        figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(110, 52) });
        return new PathGeometry { Figures = { figure } };
    }

    // ---- The yellow triangle: it bounces in, then wobbles ----

    private static FrameworkElement WarningPicture()
    {
        const double size = 150;
        var triangle = new Polygon
        {
            Points = new PointCollection { new Windows.Foundation.Point(75, 12), new Windows.Foundation.Point(142, 132), new Windows.Foundation.Point(8, 132) },
            Fill = new SolidColorBrush(Yellow), Stroke = new SolidColorBrush(Yellow), StrokeThickness = 14, StrokeLineJoin = PenLineJoin.Round,
        };
        var dark = new SolidColorBrush(Color.FromArgb(255, 40, 32, 0));
        var bar = new Rectangle { Width = 12, Height = 48, RadiusX = 6, RadiusY = 6, Fill = dark };
        Canvas.SetLeft(bar, 69); Canvas.SetTop(bar, 54);
        var dot = new Ellipse { Width = 14, Height = 14, Fill = dark };
        Canvas.SetLeft(dot, 68); Canvas.SetTop(dot, 108);

        var canvas = new Canvas { Width = size, Height = size };
        canvas.Children.Add(triangle);
        canvas.Children.Add(bar);
        canvas.Children.Add(dot);
        var host = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.65) };
        var scale = new ScaleTransform { ScaleX = 0, ScaleY = 0 };
        var rotate = new RotateTransform { Angle = 0 };
        host.RenderTransform = new TransformGroup { Children = { scale, rotate } };
        host.Children.Add(canvas);

        var story = new Storyboard();
        Animate(story, scale, "ScaleX", 0, 1, 0, 500, new BackEase { Amplitude = 0.8, EasingMode = EasingMode.EaseOut });
        Animate(story, scale, "ScaleY", 0, 1, 0, 500, new BackEase { Amplitude = 0.8, EasingMode = EasingMode.EaseOut });
        var wobble = new DoubleAnimationUsingKeyFrames();
        foreach (var (ms, angle) in new[] { (500, 0.0), (620, -10.0), (740, 9.0), (860, -6.0), (980, 4.0), (1100, 0.0) })
            wobble.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(ms), Value = angle, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        Storyboard.SetTarget(wobble, rotate);
        Storyboard.SetTargetProperty(wobble, "Angle");
        story.Children.Add(wobble);
        host.Loaded += (_, _) => story.Begin();
        return host;
    }

    private static void Animate(Storyboard story, DependencyObject target, string property, double from, double to, int beginMs, int durationMs, EasingFunctionBase ease, bool autoReverse = false)
    {
        var animation = new DoubleAnimation
        {
            From = from, To = to, BeginTime = TimeSpan.FromMilliseconds(beginMs), Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = ease, AutoReverse = autoReverse, EnableDependentAnimation = true,   // a stroke's dash offset is not on the render thread
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        story.Children.Add(animation);
    }
}
