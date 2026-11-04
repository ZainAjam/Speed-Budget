using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpeedBudget.App.ViewModels;
using Windows.System;
using Windows.UI.Core;

namespace SpeedBudget.App.Views;

/// <summary>
/// A drop-down for choosing a budget item, with a search box at the top and what is left on each item beside it.
/// When nothing matches what was typed, an "Add" row offers to create it as a new item in a category of the
/// user's choice, so a transaction never has to be abandoned halfway.
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

    public static readonly DependencyProperty AllowCreateProperty =
        DependencyProperty.Register(nameof(AllowCreate), typeof(bool), typeof(ItemPicker), new PropertyMetadata(true));

    public static readonly DependencyProperty AllowSplitProperty =
        DependencyProperty.Register(nameof(AllowSplit), typeof(bool), typeof(ItemPicker), new PropertyMetadata(false));

    public static readonly DependencyProperty ValueOverrideProperty =
        DependencyProperty.Register(nameof(ValueOverride), typeof(string), typeof(ItemPicker),
            new PropertyMetadata("", (d, _) => ((ItemPicker)d).ShowSelection()));

    /// <summary>Raised when the user picks the "Split" row, to spread the transaction over several budget items.</summary>
    public event EventHandler? SplitRequested;

    /// <summary>Raised with the typed name when the user asks to add a new item. The owner creates it and selects it.</summary>
    public event EventHandler<string>? CreateRequested;

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

    /// <summary>Whether the "add a new item" row is offered. Off where a second dialog could not be shown.</summary>
    public bool AllowCreate
    {
        get => (bool)GetValue(AllowCreateProperty);
        set => SetValue(AllowCreateProperty, value);
    }

    /// <summary>Whether a "Split between several items" row is offered at the top of the list.</summary>
    public bool AllowSplit
    {
        get => (bool)GetValue(AllowSplitProperty);
        set => SetValue(AllowSplitProperty, value);
    }

    /// <summary>Text shown in place of the selected item, such as "Split (3 lines)", while it is not empty.</summary>
    public string ValueOverride
    {
        get => (string)GetValue(ValueOverrideProperty);
        set => SetValue(ValueOverrideProperty, value);
    }

    public ItemPicker()
    {
        InitializeComponent();
        ShowSelection();
    }

    private void ShowSelection()
    {
        var overridden = !string.IsNullOrEmpty(ValueOverride);
        ValueText.Text = overridden ? ValueOverride : SelectedItem?.Name ?? "";
        ValueText.Opacity = !overridden && SelectedItem?.Id is null ? 0.6 : 1;
    }

    private void ShowHeader()
    {
        HeaderText.Text = Header;
        HeaderText.Visibility = string.IsNullOrEmpty(Header) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Flyout_Opened(object? sender, object e)
    {
        SearchBox.Text = "";
        Refresh();
        SearchBox.Focus(FocusState.Programmatic);
    }

    private List<ItemOption> Matches(string text)
    {
        var all = ItemsSource?.ToList() ?? new List<ItemOption>();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return all.Where(o => words.All(w => o.Name.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private bool Exists(string typed) =>
        (ItemsSource ?? Enumerable.Empty<ItemOption>()).Any(o => o.Id is not null && string.Equals(o.ShortName, typed, StringComparison.OrdinalIgnoreCase));

    /// <summary>Lists the matches, with an "Add" row first when the typed text isn't already an item.</summary>
    private void Refresh()
    {
        var typed = SearchBox.Text.Trim();
        var rows = Matches(typed);
        if (AllowCreate && typed != "" && !Exists(typed))
            rows.Insert(0, new ItemOption(null, $"➕ Add “{typed}” as a new budget item…", null) { IsCreateRow = true });
        if (AllowSplit) rows.Insert(0, new ItemOption(null, "Split between several items…", null) { IsSplitRow = true });
        ResultList.ItemsSource = rows;
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Search_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var typed = SearchBox.Text.Trim();
        if (e.Key == VirtualKey.Down)
        {
            ResultList.Focus(FocusState.Keyboard);
            e.Handled = true;
            return;
        }
        if (e.Key != VirtualKey.Enter) return;

        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        var matches = Matches(typed);
        var exact = matches.FirstOrDefault(o => o.Id is not null && string.Equals(o.ShortName, typed, StringComparison.OrdinalIgnoreCase));
        var firstReal = matches.FirstOrDefault(o => o.Id is not null);

        if (AllowCreate && typed != "" && ctrl && exact is null) Create(typed);
        else if (exact is not null) Choose(exact);
        else if (typed == "" && matches.Count > 0) Choose(matches[0]);
        else if (firstReal is not null) Choose(firstReal);
        else if (AllowCreate && typed != "") Create(typed);
        else return;
        e.Handled = true;
    }

    private void Result_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ItemOption option) return;
        if (option.IsSplitRow)
        {
            PickerFlyout.Hide();
            SplitRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (option.IsCreateRow) Create(SearchBox.Text.Trim());
        else Choose(option);
    }

    private void Choose(ItemOption option)
    {
        SelectedItem = option;
        PickerFlyout.Hide();
    }

    private void Create(string name)
    {
        PickerFlyout.Hide();
        CreateRequested?.Invoke(this, name);
    }
}
