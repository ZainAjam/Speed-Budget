using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.Data;

namespace SpeedBudget.App.Views;

/// <summary>Edits a counterparty's name and icon, from the pencil beside it in the counterparty drop-down.</summary>
public sealed class PayeeEditDialog : ContentDialog
{
    private readonly TextBox _name = new() { Header = "Name" };
    private readonly IconPicker _icon = new() { Header = "Icon shown in front of the name" };

    public string PayeeName => _name.Text.Trim();
    public string Emoji => _icon.SelectedEmoji;

    public PayeeEditDialog(string name, string emoji)
    {
        Title = "Edit counterparty";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _name.Text = name;
        _icon.SelectedEmoji = emoji;
        _name.TextChanged += (_, _) => IsPrimaryButtonEnabled = PayeeName != "";

        Content = new StackPanel
        {
            Spacing = 12, MinWidth = 380,
            Children =
            {
                new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Text = "Renaming a counterparty renames it on all of its transactions." },
                _name,
                _icon,
            },
        };
        Opened += (_, _) => { _name.Focus(FocusState.Programmatic); _name.SelectAll(); };
    }
}

/// <summary>Edits a budget item (name and icon) and its budget group (which group it is in, and that group's name), from the pencil beside it in the budget item drop-down.</summary>
public sealed class ItemEditDialog : ContentDialog
{
    private sealed record Choice(GroupEntity Group)
    {
        public string Display => Group.Name;
    }

    private readonly TextBox _item = new() { Header = "Budget item name" };
    private readonly IconPicker _icon = new() { Header = "Icon shown in front of the name" };
    private readonly ComboBox _group = new() { Header = "Budget group", HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = nameof(Choice.Display) };
    private readonly TextBox _groupName = new() { Header = "Name of this budget group" };

    public string ItemName => _item.Text.Trim();
    public string Emoji => _icon.SelectedEmoji;
    public Guid GroupId => (_group.SelectedItem as Choice)?.Group.Id ?? Guid.Empty;
    public string GroupName => _groupName.Text.Trim();

    /// <param name="groups">The groups the item may be in (those of its own kind).</param>
    public ItemEditDialog(IReadOnlyList<GroupEntity> groups, string itemName, string emoji, Guid groupId)
    {
        Title = "Edit budget item";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _item.Text = itemName;
        _icon.SelectedEmoji = emoji;
        var choices = groups.Select(g => new Choice(g)).ToList();
        _group.ItemsSource = choices;
        _group.SelectedIndex = Math.Max(0, choices.FindIndex(c => c.Group.Id == groupId));
        _groupName.Text = (_group.SelectedItem as Choice)?.Group.Name ?? "";

        void Validate() => IsPrimaryButtonEnabled = ItemName != "" && GroupName != "" && GroupId != Guid.Empty;
        _item.TextChanged += (_, _) => Validate();
        _groupName.TextChanged += (_, _) => Validate();
        _group.SelectionChanged += (_, _) =>
        {
            _groupName.Text = (_group.SelectedItem as Choice)?.Group.Name ?? "";
            Validate();
        };

        Content = new StackPanel
        {
            Spacing = 12, MinWidth = 400,
            Children =
            {
                _item,
                _icon,
                _group,
                _groupName,
                new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Text = "Choose another group to move the item there. Changing the group's name renames it for every item in it." },
            },
        };
        Opened += (_, _) => { _item.Focus(FocusState.Programmatic); _item.SelectAll(); };
    }
}
