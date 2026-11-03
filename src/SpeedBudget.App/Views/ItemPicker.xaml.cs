using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpeedBudget.App.ViewModels;
using Windows.System;

namespace SpeedBudget.App.Views;

/// <summary>
/// A drop-down for choosing a budget item, with a search box at the top of the list. Typing narrows the list;
/// Enter picks the first match.
/// </summary>
public sealed partial class ItemPicker : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<ItemOption>), typeof(ItemPicker), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(ItemOption), typeof(ItemPicker),
            new PropertyMetadata(null, (d, _) => ((ItemPicker)d).ShowSelection()));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(ItemPicker),
            new PropertyMetadata("", (d, _) => ((ItemPicker)d).ShowHeader()));

    public IEnumerable<ItemOption>? ItemsSource
    {
        get => (IEnumerable<ItemOption>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ItemOption? SelectedItem
    {
        get => (ItemOption?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public ItemPicker()
    {
        InitializeComponent();
        ShowSelection();
    }

    private void ShowSelection()
    {
        ValueText.Text = SelectedItem?.Name ?? "";
        ValueText.Opacity = SelectedItem?.Id is null ? 0.6 : 1;
    }

    private void ShowHeader()
    {
        HeaderText.Text = Header;
        HeaderText.Visibility = string.IsNullOrEmpty(Header) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Flyout_Opened(object? sender, object e)
    {
        SearchBox.Text = "";
        ResultList.ItemsSource = ItemsSource?.ToList();
        SearchBox.Focus(FocusState.Programmatic);
    }

    private IReadOnlyList<ItemOption> Matches(string text)
    {
        var all = ItemsSource?.ToList() ?? new List<ItemOption>();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return all.Where(o => words.All(w => o.Name.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => ResultList.ItemsSource = Matches(SearchBox.Text);

    private void Search_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        // Skip the "(no item)" entry when the user has typed something, so Enter picks a real match.
        var matches = Matches(SearchBox.Text);
        var pick = string.IsNullOrWhiteSpace(SearchBox.Text) ? matches.FirstOrDefault() : matches.FirstOrDefault(m => m.Id is not null) ?? matches.FirstOrDefault();
        if (pick is null) return;
        Choose(pick);
        e.Handled = true;
    }

    private void Result_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ItemOption option) Choose(option);
    }

    private void Choose(ItemOption option)
    {
        SelectedItem = option;
        PickerButton.Flyout.Hide();
    }
}
