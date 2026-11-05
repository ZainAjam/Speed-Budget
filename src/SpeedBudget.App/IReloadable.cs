namespace SpeedBudget.App;

/// <summary>A page that can re-read its data in place, which is how it shows the result of an undo or redo.</summary>
public interface IReloadable
{
    Task ReloadAsync();
}
