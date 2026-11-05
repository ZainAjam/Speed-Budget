using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SpeedBudget.App.Views;

/// <summary>Lists every keyboard shortcut in the app, grouped by where it works.</summary>
public sealed partial class KeyboardShortcutsPage : Page
{
    private static readonly (string Section, (string Keys, string What)[] Rows)[] Sections =
    [
        ("Anywhere", [
            ("Ctrl + 1", "Open Plan your spending"),
            ("Ctrl + 2", "Open the first account under Transactions"),
            ("Ctrl + 3", "Open Accounts"),
            ("Ctrl + 4", "Open Counterparties"),
            ("Ctrl + 5", "Open Budget Items"),
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

    public KeyboardShortcutsPage()
    {
        InitializeComponent();
        var divider = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
        foreach (var (section, rows) in Sections)
        {
            Host.Children.Add(new Border { Height = 1, Background = divider, Margin = new Thickness(0, 8, 0, 0) });
            Host.Children.Add(new TextBlock { Text = section, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
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
                Host.Children.Add(row);
            }
        }
    }
}
