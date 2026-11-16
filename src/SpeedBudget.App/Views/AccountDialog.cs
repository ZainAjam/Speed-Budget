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

    // A tracking account (one outside the budget) can be kept either by its transactions or by its balances, as a retirement fund usually is.
    private readonly ComboBox _how = new() { Header = "How do you keep track of it?", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _howNote = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 };

    public string AccountName => _name.Text.Trim();

    /// <summary>True when the account is kept by the balances typed in (daily, for instance) instead of by its transactions.</summary>
    public bool TrackBalances => !_onBudget.IsOn && _how.SelectedIndex == 1;
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
        _how.Items.Add("Record each transaction, like any other account");
        _how.Items.Add("Record its balance: daily balances, withdrawals, gains and losses");
        _how.SelectedIndex = 0;
        body.Children.Add(_how);
        body.Children.Add(_howNote);
        body.Children.Add(_opening);

        void ShowHow()
        {
            var tracking = !_onBudget.IsOn;
            _how.Visibility = _howNote.Visibility = tracking ? Visibility.Visible : Visibility.Collapsed;
            _howNote.Text = TrackBalances
                ? "You type in the balance on a date (usually every day) and anything withdrawn, and the daily changes, total changes, gains and losses are worked out for you."
                : "You enter its outflows and inflows yourself, in the transactions table.";
            _opening.Visibility = TrackBalances ? Visibility.Collapsed : Visibility.Visible;
        }
        _onBudget.Toggled += (_, _) => ShowHow();
        _how.SelectionChanged += (_, _) => ShowHow();

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
            _how.SelectedIndex = existing.TrackBalances ? 1 : 0;
            _opening.Value = (double)AppServices.ToRands(existing.OpeningBalance);
            _closed = new ToggleSwitch { Header = "Closed", OnContent = "Closed, hidden from new entries", OffContent = "Open", IsOn = existing.IsClosed };
            body.Children.Add(_closed);
        }

        Content = body;
        ShowHow();
        IsPrimaryButtonEnabled = AccountName != "";
        _name.TextChanged += (_, _) => IsPrimaryButtonEnabled = AccountName != "";
        Opened += (_, _) => { _name.Focus(FocusState.Programmatic); _name.SelectAll(); };
    }
}
