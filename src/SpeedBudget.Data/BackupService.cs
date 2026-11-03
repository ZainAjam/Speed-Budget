using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SpeedBudget.Data;

/// <summary>Keeps dated copies of the database file, so a bad edit or a failing disk never loses the budget.</summary>
public static class BackupService
{
    public static string BackupsDirectory(string dataDirectory) => Path.Combine(dataDirectory, "backups");

    /// <summary>Copies the database into the backups folder using SQLite's own backup, which is safe while the app is open.</summary>
    public static string CreateBackup(SpeedBudgetDb db, string dataDirectory, DateTime? now = null)
    {
        var folder = BackupsDirectory(dataDirectory);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"budget-{(now ?? DateTime.Now):yyyyMMdd-HHmmss}.db");

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

    public static DateTime? LastBackupTime(string dataDirectory)
    {
        var newest = BackupFiles(dataDirectory).FirstOrDefault();
        return newest is null ? null : File.GetLastWriteTime(newest);
    }

    /// <summary>Makes a backup if the last one is older than the interval, then keeps only the newest few.</summary>
    public static string? BackupIfDue(SpeedBudgetDb db, string dataDirectory, TimeSpan interval, int keep, DateTime? now = null)
    {
        var clock = now ?? DateTime.Now;
        if (LastBackupTime(dataDirectory) is { } last && clock - last < interval) return null;
        var path = CreateBackup(db, dataDirectory, clock);
        Prune(dataDirectory, keep);
        return path;
    }

    /// <summary>Deletes all but the newest backups.</summary>
    public static void Prune(string dataDirectory, int keep)
    {
        foreach (var old in BackupFiles(dataDirectory).Skip(keep)) File.Delete(old);
    }

    /// <summary>Backup files, newest first. The timestamp in the name sorts in time order.</summary>
    private static IEnumerable<string> BackupFiles(string dataDirectory)
    {
        var folder = BackupsDirectory(dataDirectory);
        if (!Directory.Exists(folder)) return Enumerable.Empty<string>();
        return Directory.EnumerateFiles(folder, "budget-*.db").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal);
    }
}
