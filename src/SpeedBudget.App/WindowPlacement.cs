using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace SpeedBudget.App;

/// <summary>Remembers where the window was and how big, and puts it back there the next time the app opens.</summary>
internal static class WindowPlacement
{
    private sealed record Saved(int X, int Y, int Width, int Height, bool Maximized);

    private static string FilePath => Path.Combine(AppServices.DataDirectory, "window.json");

    public static void Attach(Window window)
    {
        var app = window.AppWindow;
        Restore(app);

        // While maximized the window reports the whole screen, so keep the last ordinary size to come back to.
        var normal = Bounds(app);
        app.Changed += (_, e) =>
        {
            if ((e.DidSizeChange || e.DidPositionChange) && app.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
                normal = Bounds(app);
        };
        window.Closed += (_, _) => Save(app, normal);
    }

    private static RectInt32 Bounds(AppWindow app) => new(app.Position.X, app.Position.Y, app.Size.Width, app.Size.Height);

    private static void Restore(AppWindow app)
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath));
            if (saved is null || saved.Width < 400 || saved.Height < 300) return;

            // Ignore a position that is no longer on any screen, such as after a monitor was unplugged.
            var rect = new RectInt32(saved.X, saved.Y, saved.Width, saved.Height);
            var work = DisplayArea.GetFromRect(rect, DisplayAreaFallback.Nearest).WorkArea;
            var visibleWidth = Math.Min(rect.X + rect.Width, work.X + work.Width) - Math.Max(rect.X, work.X);
            var titleBarOnScreen = rect.Y >= work.Y - 10 && rect.Y < work.Y + work.Height - 40;
            if (visibleWidth < 120 || !titleBarOnScreen) return;

            app.MoveAndResize(rect);
            if (saved.Maximized && app.Presenter is OverlappedPresenter presenter) presenter.Maximize();
        }
        catch { /* a missing or damaged file just means the window opens at its default size */ }
    }

    private static void Save(AppWindow app, RectInt32 normal)
    {
        try
        {
            var state = (app.Presenter as OverlappedPresenter)?.State;
            var saved = new Saved(normal.X, normal.Y, normal.Width, normal.Height, state == OverlappedPresenterState.Maximized);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(saved));
        }
        catch { /* not being able to remember the window must never get in the way of closing */ }
    }
}
