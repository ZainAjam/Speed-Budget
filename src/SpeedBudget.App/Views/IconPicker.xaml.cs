using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpeedBudget;
using Windows.System;

namespace SpeedBudget.App.Views;

/// <summary>
/// A drop-down for choosing the icon in front of a budget item's name, from the full emoji collection. Each icon is
/// listed with its name and group, and the search box matches names and keywords, so "car" finds 🚗 (automobile).
/// </summary>
public sealed partial class IconPicker : UserControl
{
    public static readonly DependencyProperty SelectedEmojiProperty =
        DependencyProperty.Register(nameof(SelectedEmoji), typeof(string), typeof(IconPicker),
            new PropertyMetadata("", (d, _) => ((IconPicker)d).ShowSelection()));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(IconPicker),
            new PropertyMetadata("", (d, _) => ((IconPicker)d).ShowHeader()));

    /// <summary>The chosen emoji, or an empty string for no icon.</summary>
    public string SelectedEmoji
    {
        get => (string)GetValue(SelectedEmojiProperty);
        set => SetValue(SelectedEmojiProperty, value ?? "");
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

        // The full collection may arrive after the page is shown; refresh the name beside the chosen icon when it does.
        Loaded += (_, _) => Emojis.Changed += OnCatalogChanged;
        Unloaded += (_, _) => Emojis.Changed -= OnCatalogChanged;
    }

    private void OnCatalogChanged() => ShowSelection();

    private void ShowSelection()
    {
        if (string.IsNullOrEmpty(SelectedEmoji))
        {
            ValueText.Text = "No icon";
            ValueText.Opacity = 0.6;
            return;
        }
        ValueText.Text = Emojis.Find(SelectedEmoji) is { } choice ? choice.Display : SelectedEmoji;
        ValueText.Opacity = 1;
    }

    private void ShowHeader()
    {
        HeaderText.Text = Header;
        HeaderText.Visibility = string.IsNullOrEmpty(Header) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Flyout_Opened(object? sender, object e)
    {
        SourceNote.Text = Emojis.IsFullCollection
            ? $"{Emojis.Choices.Count - 1:N0} icons from the open-source Emojibase collection."
            : "Showing a short built-in list. The full collection of about 1,900 icons downloads when the app can reach the internet.";
        SearchBox.Text = "";
        ResultList.ItemsSource = Emojis.Choices;
        SearchBox.Focus(FocusState.Programmatic);
    }

    private static IReadOnlyList<EmojiChoice> Matches(string text) =>
        Core.EmojiCatalogData.Search(Emojis.Choices, c => c.Entry, text);

    private void Search_TextChanged(object sender, TextChangedEventArgs e) =>
        ResultList.ItemsSource = string.IsNullOrWhiteSpace(SearchBox.Text) ? Emojis.Choices : Matches(SearchBox.Text);

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
        SelectedEmoji = choice.Emoji;
        PickerFlyout.Hide();
    }
}
