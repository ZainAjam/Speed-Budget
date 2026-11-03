using Microsoft.UI.Xaml;

namespace SpeedBudget.App;

public partial class App : Application
{
    /// <summary>The main window, needed to attach file pickers to it.</summary>
    public static Window? MainWindow { get; private set; }

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.Initialize();
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
