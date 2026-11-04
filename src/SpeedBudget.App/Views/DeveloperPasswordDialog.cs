using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SpeedBudget.App.Views;

/// <summary>
/// Asks for the developer password before the Developer section opens. This only keeps the tools out of the way of
/// everyday use: the password is in the program itself, so it is not a security measure.
/// </summary>
public sealed class DeveloperPasswordDialog : ContentDialog
{
    private const string Password = "BuildABudget";

    private readonly PasswordBox _box = new() { PlaceholderText = "Password", Header = "Developer password" };
    private readonly TextBlock _error = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
    };

    public DeveloperPasswordDialog()
    {
        Title = "Developer";
        PrimaryButtonText = "Open";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        Content = new StackPanel { Spacing = 12, MinWidth = 340, Children = { _box, _error } };
        Opened += (_, _) => _box.Focus(FocusState.Programmatic);
        PrimaryButtonClick += (_, args) =>
        {
            if (_box.Password == Password) return;
            _error.Text = "That is not the developer password.";
            _box.Password = "";
            args.Cancel = true;   // stay open so it can be tried again
        };
    }
}
