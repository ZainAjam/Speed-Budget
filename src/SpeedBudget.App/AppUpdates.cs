using Velopack;
using Velopack.Sources;

namespace SpeedBudget.App;

public enum UpdateState { NotInstalled, UpToDate, Available, Failed }

/// <summary>The outcome of looking for an update. <see cref="Info"/> is set when one is available.</summary>
public sealed record UpdateCheck(UpdateState State, string Message, string? NewVersion = null, UpdateInfo? Info = null, string? Notes = null);

/// <summary>
/// Looks for, downloads and applies new versions of the installed app. The update source is a web address or a
/// folder (such as a shared or OneDrive folder) holding the files that scripts/package.ps1 produces, or a GitHub
/// repository whose Releases hold them.
/// </summary>
public static class AppUpdates
{
    private const string SourceKey = "updateSource";

    /// <summary>Where new releases are published. Used unless a different source has been entered under Settings.</summary>
    public const string DefaultSource = "https://github.com/ZainAjam/Speed-Budget";

    public static async Task<string> GetSourceAsync() =>
        (AppSettings.Get(SourceKey) ?? await AppServices.Store.GetLocalSettingAsync(SourceKey)) is { } saved && !string.IsNullOrWhiteSpace(saved) ? saved : DefaultSource;

    public static Task SetSourceAsync(string source)
    {
        AppSettings.Set(SourceKey, source.Trim());
        return Task.CompletedTask;
    }

    /// <summary>A GitHub repository address uses that repository's Releases, pre-releases included while the app is below 1.0; anything else is a web address or a folder.</summary>
    private static UpdateManager Open(string source)
    {
        source = source.Trim();
        return source.Contains("github.com", StringComparison.OrdinalIgnoreCase)
            ? new UpdateManager(new GithubSource(source, null, true))
            : new UpdateManager(source);
    }

    /// <summary>True when this copy was set up by the installer. A copy run straight from a build folder cannot update itself.</summary>
    public static bool IsInstalled
    {
        get
        {
            // Development runs (a Debug build, or a run on test data) never ask GitHub for updates: every start and every timer tick would count against its rate limit.
#if DEBUG
            return false;
#else
            if (Environment.GetEnvironmentVariable("SPEEDBUDGET_DATA") is { Length: > 0 }) return false;
            try { return new UpdateManager("https://localhost/none").IsInstalled; }
            catch { return false; }
#endif
        }
    }

    public static async Task<UpdateCheck> CheckAsync()
    {
        if (!IsInstalled) return new UpdateCheck(UpdateState.NotInstalled, "This copy was not set up by the installer, so it cannot update itself.");

        var source = await GetSourceAsync();

        try
        {
            var info = await Open(source).CheckForUpdatesAsync();
            if (info is null) return new UpdateCheck(UpdateState.UpToDate, "You have the latest version.");
            var version = info.TargetFullRelease.Version.ToString();
            return new UpdateCheck(UpdateState.Available, $"Version {version} is available.", version, info, info.TargetFullRelease.NotesMarkdown);
        }
        catch (Exception ex)
        {
            return new UpdateCheck(UpdateState.Failed, $"Could not check for updates: {ex.Message}");
        }
    }

    /// <summary>Downloads the update, then closes the app, installs it and starts the new version.</summary>
    public static async Task DownloadAndRestartAsync(UpdateInfo info, Action<int>? progress = null)
    {
        var manager = Open(await GetSourceAsync());
        await manager.DownloadUpdatesAsync(info, progress);
        manager.ApplyUpdatesAndRestart(info);
    }
}
