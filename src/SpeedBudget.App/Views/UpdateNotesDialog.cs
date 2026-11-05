using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SpeedBudget.App.Views;

/// <summary>
/// Shows what changed in a new version, so it can be read before downloading and updating. Pressing "Update and restart"
/// is the primary result; "Not now" leaves things as they are.
/// </summary>
public sealed class UpdateNotesDialog : ContentDialog
{
    public UpdateNotesDialog(string version, string? notes)
    {
        Title = $"What's changed in {version}";
        PrimaryButtonText = "Update and restart";
        CloseButtonText = "Not now";
        DefaultButton = ContentDialogButton.Primary;
        Resources["ContentDialogMaxWidth"] = 720.0;

        var panel = new StackPanel { Spacing = 8, MinWidth = 520 };
        if (string.IsNullOrWhiteSpace(notes))
            panel.Children.Add(new TextBlock { Text = "No change notes were published for this version.", Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
        else
            foreach (var element in Render(notes)) panel.Children.Add(element);

        Content = new ScrollViewer { Content = panel, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>Turns the simple markdown of a change log (headings, bullets and plain lines) into text on screen.</summary>
    internal static IEnumerable<UIElement> Render(string markdown)
    {
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Trim() == "") continue;

            if (line.TrimStart().StartsWith('#'))
            {
                yield return new TextBlock
                {
                    Text = line.TrimStart('#', ' ').Trim(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
                    Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                };
            }
            else if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
            {
                var bullet = new Grid { ColumnSpacing = 8 };
                bullet.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                bullet.ColumnDefinitions.Add(new ColumnDefinition());
                var dot = new TextBlock { Text = "•" };
                var text = new TextBlock { Text = Plain(line.TrimStart()[2..]), TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(text, 1);
                bullet.Children.Add(dot);
                bullet.Children.Add(text);
                yield return bullet;
            }
            else yield return new TextBlock { Text = Plain(line), TextWrapping = TextWrapping.Wrap };
        }
    }

    /// <summary>Drops the markdown marks around bold and code, which are shown as plain text.</summary>
    private static string Plain(string text) => text.Replace("**", "").Replace("`", "");
}
