using SpeedBudget.Data;

namespace SpeedBudget.App;

/// <summary>Holds the one local database and store shared by every page.</summary>
public static class AppServices
{
    public static SpeedBudgetDb Db { get; private set; } = null!;
    public static BudgetStore Store { get; private set; } = null!;
    public static WatcherService Watcher { get; private set; } = null!;

    /// <summary>Raised when accounts are added, renamed, closed or deleted, so menus listing them can refresh.</summary>
    public static event Action? AccountsChanged;

    public static void RaiseAccountsChanged() => AccountsChanged?.Invoke();

    public static string DataDirectory => LocalDatabase.DefaultDirectory;

    /// <summary>How the left panel behaves: "show" (always open), "hover" (a strip of icons that opens under the pointer) or "hide" (out of sight, opened with a small menu button).</summary>
    public static string PanelMode { get; private set; } = "hover";

    public static event Action? InterfaceChanged;

    /// <summary>The steps of the zoom, from 50% to 200%. The app is made smaller to fit more on screen (for example beside a banking website), or larger to read.</summary>
    public static readonly double[] ZoomSteps = [0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0];

    public static double Zoom { get; private set; } = 1.0;

    public static event Action? ZoomChanged;

    /// <summary>Zooms one step in (+1) or out (-1), or back to 100% (0).</summary>
    public static async Task StepZoomAsync(int direction)
    {
        var index = Array.FindIndex(ZoomSteps, s => Math.Abs(s - Zoom) < 0.001);
        if (index < 0) index = Array.IndexOf(ZoomSteps, 1.0);
        var next = direction == 0 ? Array.IndexOf(ZoomSteps, 1.0) : Math.Clamp(index + direction, 0, ZoomSteps.Length - 1);
        await SetZoomAsync(ZoomSteps[next]);
    }

    public static async Task SetZoomAsync(double zoom)
    {
        Zoom = zoom;
        ZoomChanged?.Invoke();
        try { await Store.SetLocalSettingAsync("zoom", ((int)Math.Round(zoom * 100)).ToString()); }
        catch { /* the zoom still applies for this run */ }
    }

    public static async Task SetPanelModeAsync(string mode)
    {
        PanelMode = mode;
        InterfaceChanged?.Invoke();
        try { await Store.SetLocalSettingAsync("panelMode", mode); }
        catch { /* the choice still applies for this run */ }
    }

    public static void Initialize()
    {
        Db = LocalDatabase.Open();
        Store = new BudgetStore(Db);

        // Budgets planned ahead used to be kept under fixed ids; this moves them to their calendar month, once.
        try { Store.MigrateLegacyPlannedMonthsAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not move planned budgets: {ex.Message}"); }
        try { Store.EnsureBudgetStartAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not set the start of the budget: {ex.Message}"); }
        try { var saved = Store.GetLocalSettingAsync("panelMode").GetAwaiter().GetResult(); if (saved is "show" or "hover" or "hide") PanelMode = saved; }
        catch { /* the panel just uses the normal mode */ }
        try
        {
            if (int.TryParse(Store.GetLocalSettingAsync("zoom").GetAwaiter().GetResult(), out var percent) && percent is >= 50 and <= 200) Zoom = percent / 100.0;
        }
        catch { /* the app just opens at 100% */ }
        // A summary the user folded on purpose stays folded; otherwise it opens at the top of the page and folds as it is scrolled.
        try
        {
            if (Store.GetLocalSettingAsync("budgetSummary").GetAwaiter().GetResult() == "0")
                SpeedBudget.App.ViewModels.SummaryView.UserCollapsed = SpeedBudget.App.ViewModels.SummaryView.Expanded = false;
        }
        catch { /* the summary just starts open */ }
        Watcher = new WatcherService(Db, LocalDatabase.OpenForReading());
        Store.StartRecordingUndo();   // from here on, what the user does can be undone with Ctrl+Z

        // The full icon collection downloads once in the background; until then a short built-in list is used.
        Emojis.UiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _ = Emojis.LoadAsync(DataDirectory);

        // A failed backup must never stop the app from opening.
        // Once a day, and only the newest 31 automatic backups are kept.
        try { BackupService.BackupIfDue(Db, DataDirectory); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Automatic backup failed: {ex.Message}"); }
    }

    /// <summary>The app shows money in rands but stores whole cents.</summary>
    public static decimal ToRands(long cents) => cents / 100m;

    public static long ToCents(double rands) => double.IsNaN(rands) ? 0 : (long)Math.Round(rands * 100, MidpointRounding.AwayFromZero);
}
