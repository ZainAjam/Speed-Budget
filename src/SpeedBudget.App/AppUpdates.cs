using Velopack;

namespace SpeedBudget.App;

public enum UpdateState { NotInstalled, NoSource, UpToDate, Available, Failed }

/// <summary>The outcome of looking for an update. <see cref="Info"/> is set when one is available.</summary>
public sealed record UpdateCheck(UpdateState State, string Message, string? NewVersion = null, UpdateInfo? Info = null);

/// <summary>
/// Looks for, downloads and applies new versions of the installed app. The update source is a web address or a
/// folder (such as a shared or OneDrive folder) holding the files that scripts/package.ps1 produces.
/// </summary>
public static class AppUpdates
{
    private const string SourceKey = "updateSource";

    public static async Task<string> GetSourceAsync() => await AppServices.Store.GetLocalSettingAsync(SourceKey) ?? "";

    public static Task SetSourceAsync(string source) => AppServices.Store.SetLocalSettingAsync(SourceKey, source.Trim());

    private static UpdateManager? Open(string source)
    {
        var manager = new UpdateManager(source.Trim());
        return manager;
    }

    /// <summary>True when this copy was set up by the installer. A copy run straight from a build folder cannot update itself.</summary>
    public static bool IsInstalled
    {
        get
        {
            try { return new UpdateManager("https://localhost/none").IsInstalled; }
            catch { return false; }
        }
    }

    public static async Task<UpdateCheck> CheckAsync()
    {
        if (!IsInstalled) return new UpdateCheck(UpdateState.NotInstalled, "This copy was not set up by the installer, so it cannot update itself.");

        var source = await GetSourceAsync();
        if (string.IsNullOrWhiteSpace(source)) return new UpdateCheck(UpdateState.NoSource, "Enter where updates are published to check for them.");

        try
        {
            var info = await Open(source)!.CheckForUpdatesAsync();
            if (info is null) return new UpdateCheck(UpdateState.UpToDate, "You have the latest version.");
            var version = info.TargetFullRelease.Version.ToString();
            return new UpdateCheck(UpdateState.Available, $"Version {version} is available.", version, info);
        }
        catch (Exception ex)
        {
            return new UpdateCheck(UpdateState.Failed, $"Could not check for updates: {ex.Message}");
        }
    }

    /// <summary>Downloads the update, then closes the app, installs it and starts the new version.</summary>
    public static async Task DownloadAndRestartAsync(UpdateInfo info, Action<int>? progress = null)
    {
        var manager = Open(await GetSourceAsync())!;
        await manager.DownloadUpdatesAsync(info, progress);
        manager.ApplyUpdatesAndRestart(info);
    }
}
