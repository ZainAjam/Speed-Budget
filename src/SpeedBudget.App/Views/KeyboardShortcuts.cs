using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SpeedBudget.App.Views;

/// <summary>Every keyboard shortcut in the app, grouped by where it works. Shown in App Settings.</summary>
public static class KeyboardShortcuts
{
    private static readonly (string Section, (string Keys, string What)[] Rows)[] Sections =
    [
        ("Anywhere", [
            ("Ctrl + 1", "Open Plan your spending"),
            ("Ctrl + 2", "Open the first account under Transactions"),
            ("Ctrl + + or Ctrl + -", "Zoom the whole app in or out (the mouse wheel with Ctrl held does the same)"),
            ("Ctrl + 0", "Back to 100% zoom"),
            ("Ctrl + Z", "Undo the last change to anything you can edit, on any screen"),
            ("Ctrl + Y or Ctrl + Shift + Z", "Redo what was just undone"),
        ]),
        ("Transactions", [
            ("Ctrl + N", "Add a new transaction row"),
            ("Ctrl + M", "Start a new month"),
            ("Enter", "Save the row being edited"),
            ("Esc", "Drop the changes to the row being edited"),
            ("Alt + Up", "Move the selected month line up"),
            ("Alt + Down", "Move the selected month line down"),
            ("Shift + drag a column divider", "Resize a column and move the rest of the table along"),
        ]),
        ("Counterparty and budget item lists", [
            ("Down", "Move from the search box to the list"),
            ("Enter", "Choose the first match, or add what was typed if nothing matches"),
            ("Ctrl + Enter", "Always add what was typed as a new entry"),
        ]),
        ("Plan your spending", [
            ("Enter", "Save the amount and leave the cell"),
            ("Esc", "Put the old amount back and leave the cell"),
            ("Up / Down", "Move to the cell above or below"),
            ("1200+350, 3000/2", "A sum can be typed into an amount"),
        ]),
    ];

    /// <summary>The list as controls: a section heading above each group, with the keys beside what they do.</summary>
    public static IEnumerable<UIElement> Build()
    {
        var divider = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
        var first = true;
        foreach (var (section, rows) in Sections)
        {
            if (!first) yield return new Border { Height = 1, Background = divider, Margin = new Thickness(0, 4, 0, 0) };
            first = false;
            yield return new TextBlock { Text = section, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] };
            foreach (var (keys, what) in rows)
            {
                var row = new Grid { ColumnSpacing = 16 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var key = new TextBlock { Text = keys, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], TextWrapping = TextWrapping.Wrap };
                var text = new TextBlock { Text = what, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
                Grid.SetColumn(text, 1);
                row.Children.Add(key);
                row.Children.Add(text);
                yield return row;
            }
        }
    }
}
