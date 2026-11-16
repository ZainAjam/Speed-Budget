using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace SpeedBudget.App.Views;

/// <summary>The edge of a column heading that can be dragged sideways to change the column's width (the pointer shows it can).</summary>
public sealed partial class ColumnGrip : Grid
{
    public ColumnGrip() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
}
