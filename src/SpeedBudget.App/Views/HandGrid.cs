using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace SpeedBudget.App.Views;

/// <summary>A grid that shows the hand pointer, for a figure that can be pressed.</summary>
public sealed partial class HandGrid : Grid
{
    public HandGrid() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
}
