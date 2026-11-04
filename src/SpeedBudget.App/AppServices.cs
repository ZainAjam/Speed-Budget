using SpeedBudget.Data;

namespace SpeedBudget.App;

/// <summary>Holds the one local database and store shared by every page.</summary>
public static class AppServices
{
    public static SpeedBudgetDb Db { get; private set; } = null!;
    public static BudgetStore Store { get; private set; } = null!;
    public static WatcherService Watcher { get; private set; } = null!;

    /// <summary>Automatic backups: at most one a day, keeping the newest two weeks.</summary>
    private static readonly TimeSpan BackupInterval = TimeSpan.FromHours(24);
    private const int BackupsToKeep = 14;

    /// <summary>Raised when accounts are added, renamed, closed or deleted, so menus listing them can refresh.</summary>
    public static event Action? AccountsChanged;

    public static void RaiseAccountsChanged() => AccountsChanged?.Invoke();

    public static string DataDirectory => LocalDatabase.DefaultDirectory;

    /// <summary>Whether the left panel hides itself until the pointer is over it. Remembered between runs.</summary>
    public static bool AutoHidePanel { get; private set; }

    public static event Action? InterfaceChanged;

    public static async Task SetAutoHidePanelAsync(bool value)
    {
        AutoHidePanel = value;
        InterfaceChanged?.Invoke();
        try { await Store.SetLocalSettingAsync("autoHidePanel", value ? "1" : "0"); }
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
        try { AutoHidePanel = Store.GetLocalSettingAsync("autoHidePanel").GetAwaiter().GetResult() == "1"; }
        catch { /* the panel just stays open */ }
        Watcher = new WatcherService(Db, LocalDatabase.OpenForReading());

        // The full icon collection downloads once in the background; until then a short built-in list is used.
        Emojis.UiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _ = Emojis.LoadAsync(DataDirectory);

        // A failed backup must never stop the app from opening.
        try { BackupService.BackupIfDue(Db, DataDirectory, BackupInterval, BackupsToKeep); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Automatic backup failed: {ex.Message}"); }
    }

    /// <summary>The app shows money in rands but stores whole cents.</summary>
    public static decimal ToRands(long cents) => cents / 100m;

    public static long ToCents(double rands) => double.IsNaN(rands) ? 0 : (long)Math.Round(rands * 100, MidpointRounding.AwayFromZero);
}
