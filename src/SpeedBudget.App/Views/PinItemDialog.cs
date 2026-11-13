using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpeedBudget.App.ViewModels;
using Windows.System;

namespace SpeedBudget.App.Views;

/// <summary>One budget item offered for the pinned-items strip, with what is left on it this month (in cents).</summary>
public sealed record PinChoice(Guid ItemId, string Name, long Left);

/// <summary>Asks which budget item to add to the strip. Pick from the list, or search and press Enter.</summary>
public sealed class PinItemDialog : ContentDialog
{
    private readonly IReadOnlyList<PinChoice> _choices;
    private readonly TextBox _search = new() { PlaceholderText = "Search budget items" };
    private readonly ListView _list = new() { MaxHeight = 340, SelectionMode = ListViewSelectionMode.Single };

    /// <summary>The item chosen, or null if the dialog was cancelled.</summary>
    public Guid? Chosen { get; private set; }

    public PinItemDialog(IReadOnlyList<PinChoice> choices)
    {
        _choices = choices;
        Title = "Add an item to the watcher strip";
        PrimaryButtonText = "Add to strip";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        IsPrimaryButtonEnabled = false;
        PrimaryButtonClick += (_, _) => Chosen = Selected()?.ItemId;

        Show(choices);
        _list.SelectionChanged += (_, _) => IsPrimaryButtonEnabled = _list.SelectedItem is not null;
        _list.DoubleTapped += (_, _) => Pick(Selected());
        _search.TextChanged += (_, _) => Filter();
        _search.KeyDown += Search_KeyDown;

        Content = new StackPanel
        {
            Spacing = 12, MinWidth = 440,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, Opacity = 0.8,
                    Text = choices.Count == 0
                        ? "Every budget item is already on the strip."
                        : "Pinned items show what is left on them this month, on the transaction screens. The figure beside each item is what is left now.",
                },
                _search,
                _list,
            },
        };
        Opened += (_, _) => _search.Focus(FocusState.Programmatic);
    }

    private PinChoice? Selected() => (_list.SelectedItem as ListViewItem)?.Tag as PinChoice;

    /// <summary>Lists the items: the name on the left, and what is left on it on the right (green, red, or plain when nothing is left).</summary>
    private void Show(IEnumerable<PinChoice> choices)
    {
        _list.Items.Clear();
        foreach (var choice in choices)
        {
            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = choice.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var left = new TextBlock
            {
                Text = Format.Money(choice.Left), Foreground = Format.Signed(choice.Left),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(left, 1);
            row.Children.Add(name);
            row.Children.Add(left);
            _list.Items.Add(new ListViewItem { Content = row, Tag = choice, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
    }

    private void Filter()
    {
        var words = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Show(_choices.Where(c => words.All(w => c.Name.Contains(w, StringComparison.OrdinalIgnoreCase))));
    }

    private void Search_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Down)
        {
            _list.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter && (Selected() ?? (_list.Items.FirstOrDefault() as ListViewItem)?.Tag as PinChoice) is PinChoice pick)
        {
            Pick(pick);
            e.Handled = true;
        }
    }

    private void Pick(PinChoice? choice)
    {
        if (choice is null) return;
        Chosen = choice.ItemId;
        Hide();
    }
}
