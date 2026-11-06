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
        UnhandledException += (_, e) => Diagnostics.LogCrash("UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Diagnostics.LogCrash("App", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Diagnostics.LogCrash("Task", e.Exception);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Diagnostics.EndSession();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.Initialize();
        Diagnostics.StartSession();
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
