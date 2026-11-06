namespace SpeedBudget.App;

/// <summary>
/// Esc pressed inside a picker's pop-up. The pop-up is not part of the page it was opened from, so the page never sees the key;
/// this tells the page (which decides whether the picker was in its table) so Esc can cancel the row being edited at any time.
/// </summary>
public static class EditEscape
{
    public static event Action<object>? Pressed;

    public static void Raise(object picker) => Pressed?.Invoke(picker);
}
