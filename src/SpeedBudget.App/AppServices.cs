using SpeedBudget.Data;

namespace SpeedBudget.App;

/// <summary>Holds the one local database and store shared by every page.</summary>
public static class AppServices
{
    public static SpeedBudgetDb Db { get; private set; } = null!;
    public static BudgetStore Store { get; private set; } = null!;
    public static WatcherService Watcher { get; private set; } = null!;

    public static void Initialize()
    {
        Db = LocalDatabase.Open();
        Store = new BudgetStore(Db);
        Watcher = new WatcherService(Db, LocalDatabase.OpenForReading());
    }

    /// <summary>The app shows money in rands but stores whole cents.</summary>
    public static decimal ToRands(long cents) => cents / 100m;

    public static long ToCents(double rands) => double.IsNaN(rands) ? 0 : (long)Math.Round(rands * 100, MidpointRounding.AwayFromZero);
}
