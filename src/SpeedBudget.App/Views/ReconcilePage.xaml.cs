using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

public sealed partial class ReconcilePage : Page
{
    public ReconcileViewModel ViewModel { get; } = new();

    /// <summary>Visible only when there are no accounts to reconcile.</summary>
    public Visibility NoAccounts => ViewModel.HasAccount ? Visibility.Collapsed : Visibility.Visible;

    public ReconcilePage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReconcileViewModel.HasAccount)) Bindings.Update();
        };
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }
}
