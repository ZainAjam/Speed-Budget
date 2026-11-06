using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.Data;

namespace SpeedBudget.App.Views;

/// <summary>Asks for the details of a bank account: to add a new one, or to change every detail of an existing one.</summary>
public sealed class AccountDialog : ContentDialog
{
    private readonly TextBox _name = new() { Header = "Name", PlaceholderText = "For example Everyday Account" };
    private readonly ComboBox _type = new() { Header = "Type", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ToggleSwitch _onBudget = new() { Header = "Counts toward the budget", OnContent = "Budget account", OffContent = "Tracking account" };
    private readonly NumberBox _opening = new() { Header = "Opening balance (R)", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden, PlaceholderText = "0.00" };
    private readonly ToggleSwitch? _closed;

    public string AccountName => _name.Text.Trim();
    public AccountType Type => (AccountType)Math.Max(0, _type.SelectedIndex);
    public bool IsOnBudget => _onBudget.IsOn;
    public long OpeningCents => AppServices.ToCents(_opening.Value);
    public bool IsClosed => _closed?.IsOn ?? false;

    /// <param name="existing">The account being edited, or null to add a new one.</param>
    public AccountDialog(AccountEntity? existing)
    {
        Title = existing is null ? "Add a bank account" : "Edit account";
        PrimaryButtonText = existing is null ? "Add account" : "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        foreach (var label in new[] { "Cheque", "Savings", "Credit card", "Cash", "Investment", "Loan" }) _type.Items.Add(label);
        Money.Apply(_opening);

        var body = new StackPanel { Spacing = 14, MinWidth = 400 };
        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8,
            Text = "A budget account affects the budget with every transaction. A tracking account only records its own transactions and balance.",
        });
        body.Children.Add(_name);
        body.Children.Add(_type);
        body.Children.Add(_onBudget);
        body.Children.Add(_opening);

        if (existing is null)
        {
            _type.SelectedIndex = 0;
            _onBudget.IsOn = true;
        }
        else
        {
            _name.Text = existing.Name;
            _type.SelectedIndex = (int)existing.Type;
            _onBudget.IsOn = existing.IsOnBudget;
            _opening.Value = (double)AppServices.ToRands(existing.OpeningBalance);
            _closed = new ToggleSwitch { Header = "Closed", OnContent = "Closed, hidden from new entries", OffContent = "Open", IsOn = existing.IsClosed };
            body.Children.Add(_closed);
        }

        Content = body;
        IsPrimaryButtonEnabled = AccountName != "";
        _name.TextChanged += (_, _) => IsPrimaryButtonEnabled = AccountName != "";
        Opened += (_, _) => { _name.Focus(FocusState.Programmatic); _name.SelectAll(); };
    }
}
