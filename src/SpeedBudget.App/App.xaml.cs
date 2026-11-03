using Microsoft.UI.Xaml;

namespace SpeedBudget.App;

public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.Initialize();
        _window = new MainWindow();
        _window.Activate();
    }
}
