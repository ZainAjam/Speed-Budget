using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.Data;
using Windows.Storage.Pickers;

namespace SpeedBudget.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        DataPath.Text = AppServices.DataDirectory;
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        AboutText.Text = $"Speed Budget {version.Split('+')[0]}";
        PanelModeBox.SelectedIndex = PanelModes.IndexOf(AppServices.PanelMode);
        ShowBackupInfo();
        ShowDeveloper();
        Loaded += async (_, _) => await ShowUpdateStateAsync();
    }

    private static readonly List<string> PanelModes = ["show", "hover", "hide"];

    private async void PanelMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PanelModeBox.SelectedIndex < 0) return;
        var mode = PanelModes[PanelModeBox.SelectedIndex];
        if (mode != AppServices.PanelMode) await AppServices.SetPanelModeAsync(mode);
    }

    private void ShowBackupInfo() =>
        BackupInfo.Text = BackupService.LastBackupTime(AppServices.DataDirectory) is { } last
            ? $"Last backup: {last:d MMM yyyy, HH:mm}"
            : "No backup has been made yet.";

    // The Developer section stays open for as long as the app runs, and is closed again by Lock or by restarting.
    private static bool s_developerOpen;

    private void ShowDeveloper()
    {
        DeveloperLocked.Visibility = s_developerOpen ? Visibility.Collapsed : Visibility.Visible;
        DeveloperOpen.Visibility = s_developerOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OpenDeveloper_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DeveloperPasswordDialog { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        s_developerOpen = true;
        ShowDeveloper();
    }

    private void LockDeveloper_Click(object sender, RoutedEventArgs e)
    {
        s_developerOpen = false;
        ShowDeveloper();
    }

    private void Report(string message, InfoBarSeverity severity)
    {
        Status.Message = message;
        Status.Severity = severity;
        Status.IsOpen = true;
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Reset the app?",
            Content = "This deletes everything: all accounts, budget items, transactions, counterparties and settings. " +
                      "The app starts again from a clean slate and the setup wizard runs." + Environment.NewLine + Environment.NewLine +
                      "A backup of your data is saved first, in the backups folder, but this cannot be undone from inside the app.",
            PrimaryButtonText = "Reset everything",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            BackupService.CreateBackup(AppServices.Db, AppServices.DataDirectory);
            await AppServices.Store.ResetAllAsync();
        }
        catch (Exception ex)
        {
            Report($"The reset failed: {ex.Message}", InfoBarSeverity.Error);
            return;
        }

        FirstRunDismissed.Value = false;
        SetupOffered.Value = false;
        AppServices.Watcher.Refresh();
        AppServices.RaiseAccountsChanged();
        (App.MainWindow as MainWindow)?.NavigateTo("budget");   // the Budget screen opens the setup wizard on an empty app
    }

    private UpdateCheck? _update;

    private async Task ShowUpdateStateAsync()
    {
        UpdateSource.Text = await AppUpdates.GetSourceAsync();
        if (!AppUpdates.IsInstalled)
        {
            UpdateStatus.Text = "This copy was not set up by the installer, so it cannot update itself. Install it with Speed Budget Setup to get updates.";
            CheckButton.IsEnabled = UpdateSource.IsEnabled = false;
        }
    }

    private async void UpdateSource_LostFocus(object sender, RoutedEventArgs e) => await AppUpdates.SetSourceAsync(UpdateSource.Text);

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        await AppUpdates.SetSourceAsync(UpdateSource.Text);
        CheckButton.IsEnabled = false;
        UpdateStatus.Text = "Checking…";
        _update = await AppUpdates.CheckAsync();
        UpdateStatus.Text = _update.Message;
        InstallButton.Visibility = _update.State == UpdateState.Available ? Visibility.Visible : Visibility.Collapsed;
        NotesButton.Visibility = InstallButton.Visibility;
        CheckButton.IsEnabled = true;
    }

    /// <summary>Read what is in the new version first; "Update and restart" in the dialog installs it.</summary>
    /// <summary>Shows every version's notes in a pop-up, read from the change log that ships with the app.</summary>
    private async void ChangeLog_Click(object sender, RoutedEventArgs e)
    {
        string text;
        try
        {
            text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md"));
            var first = text.IndexOf("\n## ", StringComparison.Ordinal);
            if (first >= 0) text = text[(first + 1)..];   // skip the note to writers at the top
        }
        catch { text = ""; }

        var panel = new StackPanel { Spacing = 8, MinWidth = 520 };
        if (text == "") panel.Children.Add(new TextBlock { Text = "The change log could not be found.", Opacity = 0.8 });
        else foreach (var element in UpdateNotesDialog.Render(text)) panel.Children.Add(element);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "Change log", CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close,
            Content = new ScrollViewer { Content = panel, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        };
        dialog.Resources["ContentDialogMaxWidth"] = 720.0;
        await dialog.ShowAsync();
    }

    private async void WhatsChanged_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not { State: UpdateState.Available } update) return;
        var dialog = new UpdateNotesDialog(update.NewVersion ?? "", update.Notes) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) InstallUpdate_Click(sender, e);
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_update?.Info is not { } info) return;
        InstallButton.IsEnabled = CheckButton.IsEnabled = false;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateStatus.Text = "Downloading the update…";
        try
        {
            // Backups are cheap and the update replaces the program, so take one first.
            BackupService.CreateBackup(AppServices.Db, AppServices.DataDirectory);
            await AppUpdates.DownloadAndRestartAsync(info, percent => DispatcherQueue.TryEnqueue(() => UpdateProgress.Value = percent));
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = $"The update failed: {ex.Message}";
            UpdateProgress.Visibility = Visibility.Collapsed;
            InstallButton.IsEnabled = CheckButton.IsEnabled = true;
        }
    }

    private async void Setup_Click(object sender, RoutedEventArgs e)
    {
        var wizard = new SetupWizardDialog { XamlRoot = XamlRoot };
        await wizard.ShowAsync();
        if (wizard.Result is { } r)
            Report($"Added {r.Accounts} accounts, {r.Groups} groups, {r.Items} items and {r.Counterparties} counterparties.", InfoBarSeverity.Success);
    }

    private void BackUp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = BackupService.CreateBackup(AppServices.Db, AppServices.DataDirectory, method: BackupMethod.Manual);
            ShowBackupInfo();
            Report($"Backed up to {path}", InfoBarSeverity.Success);
        }
        catch (Exception ex) { Report($"The backup failed: {ex.Message}", InfoBarSeverity.Error); }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var backups = BackupService.List(AppServices.DataDirectory);
        if (backups.Count == 0) { Report("There are no backups to restore yet.", InfoBarSeverity.Informational); return; }

        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 360 };
        foreach (var b in backups)
        {
            var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(4, 6, 4, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = b.Time.ToString("ddd d MMM yyyy, HH:mm") });
            var method = new TextBlock { Text = b.Method == BackupMethod.Automatic ? "Automatic" : "Manual", Opacity = 0.7 };
            Grid.SetColumn(method, 1);
            row.Children.Add(method);
            list.Items.Add(new ListViewItem { Content = row, Tag = b });
        }

        var panel = new StackPanel { Spacing = 8, MinWidth = 440 };
        panel.Children.Add(new TextBlock { Text = "Choose the backup to go back to. Your current data is kept as a manual backup first, so this can be undone by restoring that one.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        panel.Children.Add(list);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "Restore a backup", Content = panel,
            PrimaryButtonText = "Restore…", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false,
        };
        list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItem is not null;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || (list.SelectedItem as ListViewItem)?.Tag is not BackupInfo chosen) return;

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "Restore this backup?",
            Content = $"Your budget goes back to how it was on {chosen.Time:d MMM yyyy} at {chosen.Time:HH:mm} ({(chosen.Method == BackupMethod.Automatic ? "automatic" : "manual")} backup). " +
                      "Anything done since then is lost, apart from the manual backup of your current data that is saved first. Speed Budget restarts to do this.",
            PrimaryButtonText = "Restore and restart", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            BackupService.StageRestore(AppServices.DataDirectory, chosen.Path);
            var exe = Environment.ProcessPath;
            if (exe is null) throw new InvalidOperationException("The program could not be found to restart it.");
            Diagnostics.Note($"restore {Path.GetFileName(chosen.Path)}");
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            try { File.Delete(BackupService.PendingRestorePath(AppServices.DataDirectory)); } catch { }
            Report($"The restore failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void OpenBackups_Click(object sender, RoutedEventArgs e)
    {
        var folder = BackupService.BackupsDirectory(AppServices.DataDirectory);
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync("transactions", ".csv", "CSV file", () => ExportService.TransactionsCsvAsync(AppServices.Store));

    private async void ExportJson_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync("speed-budget", ".json", "JSON file", () => ExportService.EverythingJsonAsync(AppServices.Db));

    private async Task ExportAsync(string name, string extension, string description, Func<Task<string>> build)
    {
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = $"{name}-{DateTime.Now:yyyyMMdd}" };
            picker.FileTypeChoices.Add(description, new List<string> { extension });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            // UTF-8 with a byte order mark, so Excel reads accented characters correctly.
            await File.WriteAllTextAsync(file.Path, await build(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Report($"Saved {file.Path}", InfoBarSeverity.Success);
        }
        catch (Exception ex) { Report($"The export failed: {ex.Message}", InfoBarSeverity.Error); }
    }
}
