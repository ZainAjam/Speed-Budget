using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace SpeedBudget.App.Views;

/// <summary>
/// A drop-down for choosing the icon in front of a budget item's name. Each icon is listed with its name, and the
/// search box narrows the list by name, so "car" finds 🚗.
/// </summary>
public sealed partial class IconPicker : UserControl
{
    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(IconPicker),
            new PropertyMetadata(0, (d, _) => ((IconPicker)d).ShowSelection()));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(IconPicker),
            new PropertyMetadata("", (d, _) => ((IconPicker)d).ShowHeader()));

    /// <summary>Index into <see cref="Emojis.Choices"/>; 0 means no icon.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public IconPicker()
    {
        InitializeComponent();
        ShowSelection();
    }

    private void ShowSelection()
    {
        var choice = Emojis.Choices[Math.Clamp(SelectedIndex, 0, Emojis.Choices.Count - 1)];
        ValueText.Text = choice.Display;
        ValueText.Opacity = choice.Index == 0 ? 0.6 : 1;
    }

    private void ShowHeader()
    {
        HeaderText.Text = Header;
        HeaderText.Visibility = string.IsNullOrEmpty(Header) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Flyout_Opened(object? sender, object e)
    {
        SearchBox.Text = "";
        ResultList.ItemsSource = Emojis.Choices;
        SearchBox.Focus(FocusState.Programmatic);
    }

    private static List<EmojiChoice> Matches(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Emojis.Choices.Where(c => words.All(w => c.Name.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => ResultList.ItemsSource = Matches(SearchBox.Text);

    private void Search_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Down)
        {
            ResultList.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter && Matches(SearchBox.Text).FirstOrDefault() is { } first)
        {
            Choose(first);
            e.Handled = true;
        }
    }

    private void Result_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is EmojiChoice choice) Choose(choice);
    }

    private void Choose(EmojiChoice choice)
    {
        SelectedIndex = choice.Index;
        PickerFlyout.Hide();
    }
}
