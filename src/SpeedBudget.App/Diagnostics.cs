using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SpeedBudget.App;

/// <summary>
/// Keeps track of what the app was doing and of the times it stopped unexpectedly, in the data folder:
/// activity.log (the last things done, kept short), and crash.log (each time the app failed, with those last things).
/// </summary>
public static class Diagnostics
{
    private const int KeepBytes = 48_000;
    private static readonly object s_lock = new();

    private static string File_(string name) => Path.Combine(AppServices.DataDirectory, name);
    private static string Activity => File_("activity.log");
    private static string Running => File_("running.flag");

    /// <summary>Notes one thing the user or the app just did, so a later crash can say what led up to it.</summary>
    public static void Note(string what)
    {
        try
        {
            lock (s_lock)
            {
                var path = Activity;
                if (File.Exists(path) && new FileInfo(path).Length > KeepBytes)
                    File.WriteAllLines(path, File.ReadAllLines(path).TakeLast(150));
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {what}{Environment.NewLine}");
            }
        }
        catch { /* keeping notes must never get in the way */ }
    }

    private static string LastNotes(int count)
    {
        try { return File.Exists(Activity) ? string.Join(Environment.NewLine, File.ReadLines(Activity).TakeLast(count)) : "(none)"; }
        catch { return "(could not be read)"; }
    }

    /// <summary>An error the app caught as it was about to fail (or recovered from), with the last things that were done.</summary>
    public static void LogCrash(string source, Exception? error)
    {
        try
        {
            lock (s_lock)
                File.AppendAllText(File_("crash.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} v{typeof(App).Assembly.GetName().Version} [{source}] {error}{Environment.NewLine}" +
                    $"Last things done:{Environment.NewLine}{LastNotes(15)}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* writing the log must never make things worse */ }
    }

    /// <summary>
    /// Called as the app starts. If the last run did not close normally, Windows' record of that is checked: when it shows the app
    /// failed, that is written to crash.log with what was being done (some failures kill the app before it can write anything itself).
    /// </summary>
    public static void StartSession()
    {
        try
        {
            DateTime? previousStart = File.Exists(Running) ? File.GetLastWriteTime(Running) : null;
            File.WriteAllText(Running, DateTime.Now.ToString("O"));
            Note($"--- started v{typeof(App).Assembly.GetName().Version}");
            if (previousStart is { } since) _ = Task.Run(() => ExplainPreviousEnd(since));
        }
        catch { /* the notes are a convenience */ }
    }

    /// <summary>Called when the app closes normally.</summary>
    public static void EndSession()
    {
        Note("--- closed normally");
        try { File.Delete(Running); } catch { }
    }

    private static void ExplainPreviousEnd(DateTime since)
    {
        try
        {
            // Windows keeps a record of every application that fails; ask it (wevtutil ships with Windows) about this app since then.
            var from = since.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var start = new ProcessStartInfo("wevtutil",
                $"qe Application /q:\"*[System[Provider[@Name='Application Error'] and TimeCreated[@SystemTime>='{from}']]]\" /f:text /c:20")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);

            foreach (var block in Regex.Split(output, @"?
(?=Event\[)"))
            {
                if (!block.Contains("SpeedBudget.App.exe")) continue;
                var when = Regex.Match(block, @"Date: (\S+)").Groups[1].Value;
                var module = Regex.Match(block, @"Faulting module name: ([^,
]+)").Groups[1].Value;
                var code = Regex.Match(block, @"Exception code: (\S+)").Groups[1].Value;
                LogCrash("Windows crash report", new Exception($"The app stopped at {when} in {module} (exception code {code})."));
                return;
            }
            Note("--- the previous run ended without closing normally, but Windows has no crash report for it (it was probably ended from outside)");
        }
        catch (Exception ex) { Note($"--- could not check how the previous run ended: {ex.Message}"); }
    }
}
