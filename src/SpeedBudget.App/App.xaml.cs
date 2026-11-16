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
        UnhandledException += (_, e) =>
        {
            Diagnostics.LogCrash("UI", e.Exception);
            // The side panel sometimes fails with "Unspecified error" while it slides open or shut as a page is opened. It is a
            // refusal from Windows' own animation, nothing in the data is wrong, so the app carries on instead of closing.
            if (e.Exception is System.Runtime.InteropServices.COMException { HResult: unchecked((int)0x80004005) })
            {
                e.Handled = true;
                Diagnostics.Note("An 'Unspecified error' from the user interface was noted and ignored; the app carried on.");
            }
        };
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
