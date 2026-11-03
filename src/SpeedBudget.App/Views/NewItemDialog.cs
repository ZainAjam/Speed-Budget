using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.Core;
using SpeedBudget.Data;

namespace SpeedBudget.App.Views;

/// <summary>Asks which category a new budget item belongs in, so it can be created while entering a transaction.</summary>
public sealed class NewItemDialog : ContentDialog
{
    private sealed record Choice(Guid Id, string Display);

    private readonly ComboBox _category = new() { Header = "Budget category", HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = "Display" };
    private readonly TextBox _name = new() { Header = "Budget item name" };

    public Guid GroupId => (_category.SelectedItem as Choice)?.Id ?? Guid.Empty;
    public string ItemName => _name.Text.Trim();

    public NewItemDialog(IReadOnlyList<GroupEntity> groups, string suggestedName)
    {
        Title = "New budget item";
        PrimaryButtonText = "Create item";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        var choices = groups.Select(g => new Choice(g.Id, g.Kind == ItemKind.Income ? $"{g.Name} (income)" : g.Name)).ToList();
        _category.ItemsSource = choices;
        _category.SelectedIndex = groups.ToList().FindIndex(g => g.Kind == ItemKind.Expense) is var first and >= 0 ? first : 0;
        _name.Text = suggestedName;

        void Validate() => IsPrimaryButtonEnabled = GroupId != Guid.Empty && ItemName != "";
        _category.SelectionChanged += (_, _) => Validate();
        _name.TextChanged += (_, _) => Validate();
        Validate();

        Content = new StackPanel
        {
            Spacing = 12, MinWidth = 380,
            Children =
            {
                new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Text = "Choose the category it belongs in. You can set an icon, or move it later, under Budget Items." },
                _category,
                _name,
            },
        };
        Opened += (_, _) => _name.Focus(FocusState.Programmatic);
    }
}
