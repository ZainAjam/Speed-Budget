using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace SpeedBudget.App.Views;

/// <summary>
/// A thin strip on the edge of a column heading (or of a panel) that can be dragged sideways. It shows the resize
/// cursor, and reports how far the pointer has moved since the drag began, so the owner can set a new width.
/// </summary>
public sealed class ResizeGrip : Grid
{
    private double _startX;
    private bool _dragging;

    /// <summary>The drag began.</summary>
    public event EventHandler? Started;

    /// <summary>The pointer moved: the distance in pixels from where the drag began (negative is to the left).</summary>
    public event EventHandler<double>? Moved;

    /// <summary>The drag ended, so the new size can be saved.</summary>
    public event EventHandler? Completed;

    public ResizeGrip()
    {
        Width = 8;
        HorizontalAlignment = HorizontalAlignment.Right;
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);   // transparent but still catches the pointer
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _dragging = true;
            _startX = e.GetCurrentPoint(null).Position.X;
            CapturePointer(e.Pointer);
            Started?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (_dragging) Moved?.Invoke(this, e.GetCurrentPoint(null).Position.X - _startX);
        };
        PointerReleased += (_, e) => Finish(e);
        PointerCanceled += (_, e) => Finish(e);
        PointerCaptureLost += (_, e) => Finish(e);
    }

    private void Finish(PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleasePointerCaptures();
        Completed?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}
