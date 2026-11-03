using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace SpeedBudget.App;

/// <summary>
/// The entry point is written out (instead of generated) so the installer's hooks run first: when the app is started
/// by the installer or an update, Velopack handles that and exits before any window is created.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }
}
