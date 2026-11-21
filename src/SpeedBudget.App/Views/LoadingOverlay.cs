using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

/// <summary>
/// Covers the body of a table with a spinning ring and a few words while its rows are being loaded, so the page (its frame, headings and
/// buttons) can show at once instead of waiting for the rows. It is shown from the start, and fades away when <see cref="HideAsync"/> is called.
/// </summary>
public sealed class LoadingOverlay : Grid
{
    private readonly TextBlock _text = new() { Text = "Loading…", Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Center };

    public LoadingOverlay()
    {
        Background = Format.Theme("LayerFillColorDefaultBrush");
        Children.Add(new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 14,
            Children = { new ProgressRing { IsActive = true, Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Center }, _text },
        });
    }

    /// <summary>What it says under the ring.</summary>
    public string Text { get => _text.Text; set => _text.Text = value; }

    /// <summary>Covers the table again (a page that is shown again starts with it covered).</summary>
    public void Show()
    {
        Opacity = 1;
        Visibility = Visibility.Visible;
    }

    /// <summary>Fades the overlay away and takes it out of the way.</summary>
    public async Task HideAsync()
    {
        if (Visibility == Visibility.Collapsed) return;
        var done = new TaskCompletionSource();
        var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(160), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fade, this);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var story = new Storyboard();
        story.Children.Add(fade);
        story.Completed += (_, _) => done.TrySetResult();
        story.Begin();
        await Task.WhenAny(done.Task, Task.Delay(400));
        story.Stop();
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Waits until the page has been drawn once, so that it is on screen (with this overlay turning) before the rows start loading.</summary>
    public static async Task WaitForFirstFrameAsync()
    {
        var drawn = new TaskCompletionSource();
        void OnRendering(object? sender, object e) { CompositionTarget.Rendering -= OnRendering; drawn.TrySetResult(); }
        CompositionTarget.Rendering += OnRendering;
        await Task.WhenAny(drawn.Task, Task.Delay(400));   // (a window that is not drawing, say while minimised, must not hold the load up)
        CompositionTarget.Rendering -= OnRendering;
        await Task.Delay(30);   // and a moment for the ring to start turning
    }
}
