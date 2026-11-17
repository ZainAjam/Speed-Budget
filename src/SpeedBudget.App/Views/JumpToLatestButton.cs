using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SpeedBudget.App.Views;

/// <summary>
/// A round button that floats at the bottom centre of a long table and jumps to the end of it where the latest record is:
/// the top when the newest comes first, the bottom when the oldest comes first. It shows only when the table has lots of
/// records and the latest end is not already in view, and its arrow points the way it jumps.
/// </summary>
public sealed class JumpToLatestButton : Button
{
    /// <summary>How many records make a table long enough to offer the jump.</summary>
    private const int LongTable = 25;

    private readonly FontIcon _icon = new() { FontSize = 16 };
    private ListViewBase? _list;
    private ScrollViewer? _viewer;
    private Func<bool> _newestFirst = () => true;
    private Func<int> _count = () => 0;

    public JumpToLatestButton()
    {
        Content = _icon;
        Visibility = Visibility.Collapsed;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 0, 18);
        Width = Height = 40;
        Padding = new Thickness(0);
        CornerRadius = new CornerRadius(20);
        Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];
        BorderThickness = new Thickness(0);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, "Jump to the latest");
        Click += async (_, _) => await JumpAsync();
    }

    /// <summary>Ties the button to a list. <paramref name="newestFirst"/> says which end is the latest; <paramref name="count"/> how many records there are.</summary>
    public void Attach(ListViewBase list, Func<bool> newestFirst, Func<int> count)
    {
        _list = list;
        _newestFirst = newestFirst;
        _count = count;
        list.Loaded += (_, _) => Hook();
        if (list.IsLoaded) Hook();
    }

    /// <summary>Shows or hides the button again; called when the sort order or the number of records changes.</summary>
    public void Refresh()
    {
        try
        {
            Hook();
            var latestAtTop = _newestFirst();
            _icon.Glyph = latestAtTop ? "" : "";   // up arrow, down arrow
            ToolTipService.SetToolTip(this, latestAtTop ? "Jump to the top, where the latest is" : "Jump to the bottom, where the latest is");
            if (_viewer is null || _count() < LongTable) { Visibility = Visibility.Collapsed; return; }
            var atLatest = latestAtTop ? _viewer.VerticalOffset < 40 : _viewer.ScrollableHeight - _viewer.VerticalOffset < 40;
            Visibility = atLatest ? Visibility.Collapsed : Visibility.Visible;
        }
        catch { /* the button is only a convenience */ }
    }

    private void Hook()
    {
        if (_list is null) return;
        var viewer = FindScrollViewer(_list);
        if (viewer is null || ReferenceEquals(viewer, _viewer)) return;
        if (_viewer is not null) _viewer.ViewChanged -= Viewer_ViewChanged;
        _viewer = viewer;
        _viewer.ViewChanged += Viewer_ViewChanged;
    }

    private void Viewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => Refresh();

    private async Task JumpAsync()
    {
        Hook();
        if (_viewer is null) return;
        var top = _newestFirst();
        try
        {
            // Rows that are not measured yet make one jump fall short, so it goes again until the end is reached.
            for (var pass = 0; pass < 10; pass++)
            {
                _viewer.ChangeView(null, top ? 0 : _viewer.ScrollableHeight, null, disableAnimation: pass > 0);
                await Task.Delay(top ? 40 : 60);
                if (top ? _viewer.VerticalOffset < 1 : _viewer.ScrollableHeight - _viewer.VerticalOffset < 1) break;
            }
        }
        catch (Exception ex) { Diagnostics.Note($"could not jump to the latest: {ex.Message}"); }
        Refresh();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer viewer) return viewer;
            if (FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }
}
