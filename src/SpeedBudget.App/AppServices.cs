using SpeedBudget.Data;

namespace SpeedBudget.App;

/// <summary>The screens that are zoomed on their own: the Budget, and the bank account transactions.</summary>
public enum ZoomArea { Budget, Transactions }

/// <summary>Holds the open budget's database and store, shared by every page. Switching budgets restarts the app, so they never change during a run.</summary>
public static class AppServices
{
    /// <summary>The budgets on this computer.</summary>
    public static BudgetRegistry Registry { get; private set; } = null!;

    /// <summary>The budget that is open.</summary>
    public static BudgetEntry CurrentBudget { get; private set; } = null!;

    /// <summary>The folder holding the open budget's database and backups.</summary>
    public static string BudgetDirectory { get; private set; } = null!;

    public static SpeedBudgetDb Db { get; private set; } = null!;
    public static BudgetStore Store { get; private set; } = null!;
    public static WatcherService Watcher { get; private set; } = null!;

    /// <summary>Raised when accounts are added, renamed, closed or deleted, so menus listing them can refresh.</summary>
    public static event Action? AccountsChanged;

    public static void RaiseAccountsChanged() => AccountsChanged?.Invoke();

    /// <summary>The data folder: the list of budgets, each budget's folder, and things that belong to the installation (window position, icon cache).</summary>
    public static string DataDirectory => LocalDatabase.DefaultDirectory;

    /// <summary>How the left panel behaves: "show" (always open), "hover" (a strip of icons that opens under the pointer) or "hide" (out of sight, opened with a small menu button).</summary>
    public static string PanelMode { get; private set; } = "hover";

    public static event Action? InterfaceChanged;

    /// <summary>The steps of the zoom, from 50% to 200%. The app is made smaller to fit more on screen (for example beside a banking website), or larger to read.</summary>
    public static readonly double[] ZoomSteps = [0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0];

    // The Budget screen and the bank account transaction screens each have a zoom of their own. The zoom controls in the left panel
    // change the one belonging to the screen that is showing.
    public static double BudgetZoom { get; private set; } = 1.0;
    public static double TransactionsZoom { get; private set; } = 1.0;
    public static ZoomArea CurrentZoomArea { get; private set; } = ZoomArea.Budget;

    public static double ZoomOf(ZoomArea area) => area == ZoomArea.Budget ? BudgetZoom : TransactionsZoom;

    /// <summary>The zoom of the screen that is showing.</summary>
    public static double Zoom => ZoomOf(CurrentZoomArea);

    public static string ZoomAreaName => CurrentZoomArea == ZoomArea.Budget ? "the Budget screen" : "the bank account transaction screens";

    /// <summary>Says which screen the zoom controls now belong to, so they show and change that screen's zoom.</summary>
    public static void SetZoomArea(ZoomArea area)
    {
        if (area == CurrentZoomArea) return;
        CurrentZoomArea = area;
        ZoomChanged?.Invoke();
    }

    public static event Action? ZoomChanged;

    /// <summary>Zooms one step in (+1) or out (-1), or back to 100% (0).</summary>
    public static async Task StepZoomAsync(int direction)
    {
        if (direction == 0) { await SetZoomAsync(1.0); return; }
        // The next step above (or below) the current level, which may be a typed percentage between two steps.
        var next = direction > 0
            ? ZoomSteps.FirstOrDefault(s => s > Zoom + 0.001, ZoomSteps[^1])
            : ZoomSteps.LastOrDefault(s => s < Zoom - 0.001, ZoomSteps[0]);
        await SetZoomAsync(next);
    }

    public static Task SetZoomAsync(double zoom)
    {
        var area = CurrentZoomArea;
        if (area == ZoomArea.Budget) BudgetZoom = zoom; else TransactionsZoom = zoom;
        ZoomChanged?.Invoke();
        AppSettings.Set(area == ZoomArea.Budget ? "zoomBudget" : "zoomTransactions", ((int)Math.Round(zoom * 100)).ToString());
        return Task.CompletedTask;
    }

    public static Task SetPanelModeAsync(string mode)
    {
        PanelMode = mode;
        InterfaceChanged?.Invoke();
        AppSettings.Set("panelMode", mode);
        return Task.CompletedTask;
    }

    public static void Initialize()
    {
        Registry = BudgetRegistry.Load(DataDirectory);
        CurrentBudget = Registry.Current;
        BudgetDirectory = Registry.DirectoryOf(CurrentBudget);
        try { Registry.SetCurrent(CurrentBudget.Id); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not remember the budget: {ex.Message}"); }

        Db = LocalDatabase.Open(BudgetDirectory, DataDirectory);
        Store = new BudgetStore(Db);

        // An earlier version could hide budget items; every item is shown now.
        try { Store.ShowAllItemsAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not show hidden items: {ex.Message}"); }

        // Budgets planned ahead used to be kept under fixed ids; this moves them to their calendar month, once.
        try { Store.MigrateLegacyPlannedMonthsAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not move planned budgets: {ex.Message}"); }
        try { Store.EnsureBudgetStartAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not set the start of the budget: {ex.Message}"); }
        try { var saved = InstallationSetting("panelMode"); if (saved is "show" or "hover" or "hide") PanelMode = saved; }
        catch { /* the panel just uses the normal mode */ }
        try
        {
            // One zoom used to cover everything; it is the starting point for both until each has been set on its own.
            var legacy = int.TryParse(InstallationSetting("zoom"), out var old) && old is >= 50 and <= 200 ? old / 100.0 : 1.0;
            BudgetZoom = int.TryParse(InstallationSetting("zoomBudget"), out var b) && b is >= 50 and <= 200 ? b / 100.0 : legacy;
            TransactionsZoom = int.TryParse(InstallationSetting("zoomTransactions"), out var t) && t is >= 50 and <= 200 ? t / 100.0 : legacy;
        }
        catch { /* the app just opens at 100% */ }
        // A summary the user folded on purpose stays folded; otherwise it opens at the top of the page and folds as it is scrolled.
        try
        {
            if (Store.GetLocalSettingAsync("budgetSummary").GetAwaiter().GetResult() == "0")
                SpeedBudget.App.ViewModels.SummaryView.UserCollapsed = SpeedBudget.App.ViewModels.SummaryView.Expanded = false;
        }
        catch { /* the summary just starts open */ }
        Watcher = new WatcherService(Db, OpenReader());
        Store.StartRecordingUndo();   // from here on, what the user does can be undone with Ctrl+Z

        // The full icon collection downloads once in the background; until then a short built-in list is used.
        Emojis.UiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _ = Emojis.LoadAsync(DataDirectory);

        // A failed backup must never stop the app from opening.
        // Once a day, and only the newest 31 automatic backups are kept.
        try { BackupService.BackupIfDue(Db, BudgetDirectory); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Automatic backup failed: {ex.Message}"); }
    }

    /// <summary>
    /// A choice kept for the whole installation. Before there were several budgets these were kept inside the budget, so a budget
    /// that still holds one supplies it until it has been set again.
    /// </summary>
    private static string? InstallationSetting(string key) => AppSettings.Get(key) ?? Store.GetLocalSettingAsync(key).GetAwaiter().GetResult();

    /// <summary>Picks up the open budget's entry again after it has been renamed.</summary>
    public static void RefreshCurrentBudget() => CurrentBudget = Registry.Budgets.First(b => b.Id == CurrentBudget.Id);

    /// <summary>A second connection to the open budget, for background reads that must not share a context with what the user is editing.</summary>
    public static SpeedBudgetDb OpenReader() => LocalDatabase.OpenForReading(BudgetDirectory, DataDirectory);

    /// <summary>Starts the app again on the chosen budget (it is remembered as the one to open) and closes this run.</summary>
    public static void SwitchBudget(Guid id)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("The program could not be found to restart it.");
        Registry.SetCurrent(id);
        Diagnostics.Note($"switch budget {id}");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        Environment.Exit(0);
    }

    /// <summary>The app shows money in rands but stores whole cents.</summary>
    public static decimal ToRands(long cents) => cents / 100m;

    public static long ToCents(double rands) => double.IsNaN(rands) ? 0 : (long)Math.Round(rands * 100, MidpointRounding.AwayFromZero);
}
