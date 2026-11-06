using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SpeedBudget.App.Views;

/// <summary>
/// A small box asking for a name, an icon, or both: for adding or renaming a budget group or item. Leave the name header null for an
/// icon-only box, and the icon header null for a name-only one.
/// </summary>
public sealed class NameDialog : ContentDialog
{
    private readonly TextBox? _name;
    private readonly IconPicker? _icon;

    public string NameText => _name?.Text.Trim() ?? "";
    public string Emoji => _icon?.SelectedEmoji ?? "";

    public NameDialog(string title, string? note, string? nameHeader, string name, string primaryText, string? iconHeader = null, string emoji = "")
    {
        Title = title;
        PrimaryButtonText = primaryText;
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        var body = new StackPanel { Spacing = 12, MinWidth = 380 };
        if (note is not null) body.Children.Add(new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        if (nameHeader is not null)
        {
            _name = new TextBox { Header = nameHeader, Text = name };
            _name.TextChanged += (_, _) => IsPrimaryButtonEnabled = NameText != "";
            IsPrimaryButtonEnabled = NameText != "";
            body.Children.Add(_name);
        }
        if (iconHeader is not null)
        {
            _icon = new IconPicker { Header = iconHeader, SelectedEmoji = emoji };
            body.Children.Add(_icon);
        }
        Content = body;
        Opened += (_, _) => { if (_name is not null) { _name.Focus(FocusState.Programmatic); _name.SelectAll(); } };
    }
}
