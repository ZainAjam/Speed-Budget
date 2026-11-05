using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace SpeedBudget.App.Views;

/// <summary>One row of the counterparty list: a counterparty (with its icon) or the "add what was typed" row.</summary>
public sealed class PayeeListRow(string name, string text, bool isCreate)
{
    public string Name { get; } = name;
    public string Text { get; } = text;
    public bool IsCreate { get; } = isCreate;

    /// <summary>Only a real counterparty has a pencil.</summary>
    public bool CanEdit => !IsCreate;
}

/// <summary>
/// A drop-down for choosing a payee, with a search box at the top of the list. When nothing in the list matches
/// what was typed, a "Create" row adds it as a new payee without leaving the transaction being entered.
/// </summary>
public sealed partial class PayeePicker : UserControl
{
    private const string CreatePrefix = "➕ Add “";

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<string>), typeof(PayeePicker), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedPayeeProperty =
        DependencyProperty.Register(nameof(SelectedPayee), typeof(string), typeof(PayeePicker),
            new PropertyMetadata("", (d, _) => ((PayeePicker)d).ShowSelection()));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(PayeePicker),
            new PropertyMetadata("", (d, _) => ((PayeePicker)d).ShowHeader()));

    public static readonly DependencyProperty IconsProperty =
        DependencyProperty.Register(nameof(Icons), typeof(IReadOnlyDictionary<string, string>), typeof(PayeePicker), new PropertyMetadata(null));

    /// <summary>Raised when the user adds a payee that is not in the list yet, so the owner can save it.</summary>
    public event EventHandler<string>? CreateRequested;

    /// <summary>Raised with a counterparty's name when its pencil is pressed, so the owner can edit its name and icon.</summary>
    public event EventHandler<string>? EditRequested;

    /// <summary>The icon of each counterparty that has one, by name. Shown in front of the names in the list.</summary>
    public IReadOnlyDictionary<string, string>? Icons
    {
        get => (IReadOnlyDictionary<string, string>?)GetValue(IconsProperty);
        set => SetValue(IconsProperty, value);
    }

    public IEnumerable<string>? ItemsSource
    {
        get => (IEnumerable<string>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string SelectedPayee
    {
        get => (string)GetValue(SelectedPayeeProperty);
        set => SetValue(SelectedPayeeProperty, value);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public PayeePicker()
    {
        InitializeComponent();
        ShowSelection();
    }

    /// <summary>Redraws the chosen counterparty, for when its icon was edited.</summary>
    public void RefreshSelection() => ShowSelection();

    /// <summary>Puts the keyboard focus on the picker's button, so Enter or Space opens the list.</summary>
    public void FocusPicker() => PickerButton.Focus(FocusState.Keyboard);

    /// <summary>Opens the list, ready to type in, so a payee can be chosen without touching the mouse.</summary>
    public void ShowFlyout() => PickerFlyout.ShowAt(PickerButton);

    private string WithIcon(string name) =>
        Icons is not null && Icons.TryGetValue(name, out var icon) && icon != "" ? $"{icon} {name}" : name;

    private void ShowSelection()
    {
        var empty = string.IsNullOrEmpty(SelectedPayee);
        ValueText.Text = empty ? "Choose counterparty" : WithIcon(SelectedPayee);
        ValueText.Opacity = empty ? 0.6 : 1;
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

    private List<string> Matches(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (ItemsSource ?? Enumerable.Empty<string>())
            .Where(n => words.All(w => n.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>Lists the matches, with an "Add" row first when the typed text isn't already a payee.</summary>
    private void Refresh()
    {
        var typed = SearchBox.Text.Trim();
        var rows = Matches(typed).Select(n => new PayeeListRow(n, WithIcon(n), isCreate: false)).ToList();
        var exists = (ItemsSource ?? Enumerable.Empty<string>()).Any(n => string.Equals(n, typed, StringComparison.OrdinalIgnoreCase));
        if (typed != "" && !exists) rows.Insert(0, new PayeeListRow(typed, $"{CreatePrefix}{typed}”", isCreate: true));
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
        var exact = matches.FirstOrDefault(n => string.Equals(n, typed, StringComparison.OrdinalIgnoreCase));

        if (typed != "" && ctrl && exact is null) Create(typed);
        else if (exact is not null) Choose(exact);
        else if (matches.Count > 0) Choose(matches[0]);
        else if (typed != "") Create(typed);
        else return;
        e.Handled = true;
    }

    private void Result_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PayeeListRow row) return;
        if (row.IsCreate) Create(SearchBox.Text.Trim());
        else Choose(row.Name);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PayeeListRow { IsCreate: false } row) return;
        PickerFlyout.Hide();
        EditRequested?.Invoke(this, row.Name);
    }

    private void Choose(string payee)
    {
        SelectedPayee = payee;
        PickerFlyout.Hide();
    }

    private void Create(string name)
    {
        SelectedPayee = name;
        CreateRequested?.Invoke(this, name);
        PickerFlyout.Hide();
    }
}
