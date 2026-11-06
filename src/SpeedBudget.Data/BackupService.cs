using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SpeedBudget.Data;

/// <summary>How a backup came about. The method is part of the file name: budget-auto-… or budget-manual-….</summary>
public enum BackupMethod { Manual, Automatic }

/// <summary>One backup file in the backups folder.</summary>
public sealed record BackupInfo(string Path, DateTime Time, BackupMethod Method);

/// <summary>Keeps dated copies of the database file, so a bad edit or a failing disk never loses the budget.</summary>
public static class BackupService
{
    /// <summary>Automatic backups are made once a day and only this many are kept (a rolling window of days). Manual backups are never removed.</summary>
    public const int AutomaticBackupsToKeep = 31;

    private const string Stamp = "yyyyMMdd-HHmmss";

    public static string BackupsDirectory(string dataDirectory) => Path.Combine(dataDirectory, "backups");

    /// <summary>The file a pending restore is staged in; the app swaps it in the next time it starts.</summary>
    public static string PendingRestorePath(string dataDirectory) => Path.Combine(dataDirectory, "restore-pending.db");

    /// <summary>Copies the database into the backups folder using SQLite's own backup, which is safe while the app is open.</summary>
    public static string CreateBackup(SpeedBudgetDb db, string dataDirectory, DateTime? now = null, BackupMethod method = BackupMethod.Manual)
    {
        var folder = BackupsDirectory(dataDirectory);
        Directory.CreateDirectory(folder);
        var name = method == BackupMethod.Automatic ? "auto" : "manual";
        var path = Path.Combine(folder, $"budget-{name}-{(now ?? DateTime.Now).ToString(Stamp, CultureInfo.InvariantCulture)}.db");

        var source = (SqliteConnection)db.Database.GetDbConnection();
        var wasOpen = source.State == System.Data.ConnectionState.Open;
        if (!wasOpen) source.Open();
        try
        {
            using var destination = new SqliteConnection($"Data Source={path}");
            destination.Open();
            source.BackupDatabase(destination);
        }
        finally { if (!wasOpen) source.Close(); }

        // The destination connection pool can hold the new file open; release it so the file can be moved or deleted.
        SqliteConnection.ClearAllPools();
        return path;
    }

    public static DateTime? LastBackupTime(string dataDirectory) => List(dataDirectory).Select(b => (DateTime?)b.Time).FirstOrDefault();

    /// <summary>Makes the day's automatic backup if there is not one yet for today, then trims the automatic ones to the newest 31.</summary>
    public static string? BackupIfDue(SpeedBudgetDb db, string dataDirectory, DateTime? now = null)
    {
        var clock = now ?? DateTime.Now;
        if (List(dataDirectory).Any(b => b.Method == BackupMethod.Automatic && b.Time.Date == clock.Date)) return null;
        var path = CreateBackup(db, dataDirectory, clock, BackupMethod.Automatic);
        PruneAutomatic(dataDirectory);
        return path;
    }

    /// <summary>Deletes automatic backups beyond the newest 31. Manual backups are never touched.</summary>
    public static void PruneAutomatic(string dataDirectory, int keep = AutomaticBackupsToKeep)
    {
        foreach (var old in List(dataDirectory).Where(b => b.Method == BackupMethod.Automatic).Skip(keep)) File.Delete(old.Path);
    }

    /// <summary>
    /// Every backup, newest first. Files from before the method was named (budget-yyyyMMdd-HHmmss.db) count as manual,
    /// so they are never deleted automatically.
    /// </summary>
    public static IReadOnlyList<BackupInfo> List(string dataDirectory)
    {
        var folder = BackupsDirectory(dataDirectory);
        var found = new List<BackupInfo>();
        if (!Directory.Exists(folder)) return found;
        foreach (var file in Directory.EnumerateFiles(folder, "budget-*.db"))
        {
            var name = Path.GetFileNameWithoutExtension(file)["budget-".Length..];
            var method = BackupMethod.Manual;
            if (name.StartsWith("auto-", StringComparison.Ordinal)) { method = BackupMethod.Automatic; name = name["auto-".Length..]; }
            else if (name.StartsWith("manual-", StringComparison.Ordinal)) name = name["manual-".Length..];
            if (!DateTime.TryParseExact(name, Stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) continue;
            found.Add(new BackupInfo(file, time, method));
        }
        return found.OrderByDescending(b => b.Time).ToList();
    }

    /// <summary>
    /// Stages a backup to be restored: it is copied beside the database, and the app swaps it in when it next starts
    /// (the database is open now, so it cannot be replaced safely while the app runs).
    /// </summary>
    public static void StageRestore(string dataDirectory, string backupPath) =>
        File.Copy(backupPath, PendingRestorePath(dataDirectory), overwrite: true);

    /// <summary>
    /// Called before the database is opened. If a restore is staged, the current database is kept as a manual backup
    /// ("before restore") and then replaced by the staged copy. Returns true when a restore was applied.
    /// </summary>
    public static bool ApplyPendingRestore(string dataDirectory, string databasePath, DateTime? now = null)
    {
        var pending = PendingRestorePath(dataDirectory);
        if (!File.Exists(pending)) return false;

        var folder = BackupsDirectory(dataDirectory);
        Directory.CreateDirectory(folder);
        if (File.Exists(databasePath))
            File.Copy(databasePath, Path.Combine(folder, $"budget-manual-{(now ?? DateTime.Now).ToString(Stamp, CultureInfo.InvariantCulture)}.db"), overwrite: true);

        SqliteConnection.ClearAllPools();
        foreach (var extra in new[] { databasePath + "-wal", databasePath + "-shm" })
            if (File.Exists(extra)) File.Delete(extra);
        File.Move(pending, databasePath, overwrite: true);
        return true;
    }
}
