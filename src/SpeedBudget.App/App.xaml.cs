using Microsoft.UI.Xaml;

namespace SpeedBudget.App;

public partial class App : Application
{
    /// <summary>The main window, needed to attach file pickers to it.</summary>
    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();

        // If the app ever closes unexpectedly, what went wrong is written to crash.log in the data folder.
        UnhandledException += (_, e) => LogCrash("UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash("App", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => LogCrash("Task", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.Initialize();
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }

    private static void LogCrash(string source, Exception? error)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppServices.DataDirectory, "crash.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] {error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* writing the log must never make things worse */ }
    }
}
