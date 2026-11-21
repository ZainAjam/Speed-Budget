using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;
using SpeedBudget.Data;

namespace SpeedBudget.App.Views;

/// <summary>
/// Shown when an import does not reconcile and the user wants to find out why. It lists what differs between the account and the bank's file,
/// each with the fix that goes with it (use the bank's date, add the missing line, un-clear or delete the extra transaction, change the opening
/// balance), and checks again after every fix. When the account agrees with the file the dialog closes and <see cref="Resolved"/> is true.
/// </summary>
public sealed class BalanceFixDialog : ContentDialog
{
    private readonly TransactionsViewModel _vm;
    private readonly BalanceCheck _check;
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _list = new() { Spacing = 10 };
    private readonly Grid _body = new() { RowSpacing = 10 };
    private BalanceDiagnosis? _diagnosis;
    private string? _confirming;   // the fix waiting for its second press

    /// <summary>True when the fixes brought the account into line with the file.</summary>
    public bool Resolved { get; private set; }

    public BalanceFixDialog(TransactionsViewModel vm, BalanceCheck check)
    {
        _vm = vm;
        _check = check;
        Title = "Find the difference";
        CloseButtonText = "Close";
        PrimaryButtonText = "Add a balancing transaction";
        IsPrimaryButtonEnabled = false;
        DefaultButton = ContentDialogButton.Close;
        Resources["ContentDialogMaxWidth"] = 4000.0;
        Resources["ContentDialogMaxHeight"] = 4000.0;

        _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 0) };
        Grid.SetRow(scroll, 1);
        _body.Children.Add(_summary);
        _body.Children.Add(scroll);
        Content = _body;

        PrimaryButtonClick += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try { e.Cancel = true; await BalancingAsync(); }
            finally { deferral.Complete(); }
        };
        Opened += async (_, _) =>
        {
            if (XamlRoot is { } root)
            {
                _body.Width = Math.Max(640, root.Size.Width - 140);
                _body.Height = Math.Max(300, root.Size.Height - 250);
            }
            await RefreshAsync();
        };
    }

    /// <summary>Looks at the account again and shows what still differs.</summary>
    private async Task RefreshAsync()
    {
        _confirming = null;
        PrimaryButtonText = "Add a balancing transaction";
        _diagnosis = await _vm.DiagnoseBalanceAsync(_check.AccountId, _check.Lines, _check.Info.Cleared is { } stated ? (_check.Info.Date, stated) : null);
        _list.Children.Clear();
        if (_diagnosis is not { } d) { _summary.Text = "The file has no balances to compare with."; return; }

        if (d.Reconciles)
        {
            Resolved = true;
            Hide();
            return;
        }

        var more = d.Gap > 0;
        _summary.Text = $"On {d.LastDate:d MMM yyyy} the file says the balance is {Format.Money(d.FileBalance)} and Speed Budget's cleared balance is {Format.Money(d.AppBalance)}: "
            + $"{Format.Money(Math.Abs(d.Gap))} {(more ? "too much" : "too little")}. Below is what differs, with a fix for each. Speed Budget checks again after every fix, and Ctrl+Z undoes a change.";
        IsPrimaryButtonEnabled = true;

        var opening = await _vm.GetOpeningBalanceAsync(_check.AccountId);
        foreach (var item in d.Items) _list.Children.Add(CardFor(item, opening));
        if (d.Items.Count == 0)
            _list.Children.Add(Card("No single transaction explains the difference", "The file's balances and its lines do not add up to the account in a way that points at one transaction. Check the balance column of the file, or add a balancing transaction below.", []));
    }

    private UIElement CardFor(DiagnosisItem item, long opening)
    {
        var account = _check.AccountId;
        switch (item.Kind)
        {
            case DiagnosisKind.OpeningGap:
            {
                var newOpening = opening - item.Amount;
                return Card("The account and the file already differ before the file starts",
                    $"Before {item.Date:d MMM yyyy} Speed Budget's cleared balance is {Format.Money(Math.Abs(item.Amount))} {(item.Amount > 0 ? "higher" : "lower")} than the file's. This is usually the account's opening balance being different, or earlier history the file does not cover.",
                    [($"Change the opening balance to {Format.Money(newOpening)}", $"open", () => _vm.FixAsync(s => s.SetOpeningBalanceAsync(account, newOpening)), false)]);
            }
            case DiagnosisKind.DatedDifferently:
                return Card($"{item.Transaction!.Payee}, {Format.Money(item.Transaction.Amount)}: dated {item.Transaction.Date:d MMM yyyy} in Speed Budget, {item.Line!.Date:d MMM yyyy} in the file",
                    "It is the same transaction, but the dates differ, so the balances disagree on the days between.",
                    [($"Use the file's date ({item.Line.Date:d MMM yyyy})", $"date{item.Transaction.Id}", () => _vm.FixAsync(s => s.SetBankDateAsync(item.Transaction.Id, item.Line.Date)), false)]);
            case DiagnosisKind.ClearedNotInFile:
                return Card($"In Speed Budget but not in the file: {item.Transaction!.Date:d MMM yyyy}, {item.Transaction.Payee}, {Format.Money(item.Transaction.Amount)}",
                    "It is cleared in Speed Budget, but the bank's file has no such line that day. It may be a duplicate, or cleared by mistake.",
                    [("Un-clear it", $"unclear{item.Transaction.Id}", () => _vm.FixAsync(s => s.SetClearedAsync(item.Transaction.Id, false)), false),
                     ("Delete it", $"delete{item.Transaction.Id}", () => _vm.FixAsync(s => s.DeleteTransactionAsync(item.Transaction.Id)), true)]);
            default:
                return Card($"In the file but not in Speed Budget: {item.Line!.Date:d MMM yyyy}, {(item.Line.Description == "" ? "(no counterparty)" : item.Line.Description)}, {Format.Money(item.Line.Amount)}",
                    "The bank has this line but the account has no cleared transaction for it that day (it may have been left out of the import).",
                    [("Add it", $"add{item.Line.Line}", () => _vm.FixAsync(s => s.ApplyBankMatchesAsync(account, Array.Empty<BankClear>(), [item.Line])), false)]);
        }
    }

    /// <summary>One finding: what it is, what it means, and a button for each fix. A fix that deletes something needs a second press.</summary>
    private Border Card(string title, string detail, IReadOnlyList<(string Label, string Key, Func<Task> Fix, bool Confirm)> fixes)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (label, key, fix, confirm) in fixes)
        {
            var button = new Button { Content = label, Padding = new Thickness(12, 5, 12, 5) };
            button.Click += async (_, _) =>
            {
                if (confirm && _confirming != key) { _confirming = key; button.Content = "Press again to delete"; return; }
                button.IsEnabled = false;
                try { await fix(); } catch (Exception ex) { _summary.Text = $"That could not be done: {ex.Message}"; }
                await RefreshAsync();
            };
            buttons.Children.Add(button);
        }
        var text = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = detail, Opacity = 0.8, TextWrapping = TextWrapping.Wrap },
            },
        };
        if (fixes.Count > 0) text.Children.Add(buttons);
        return new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14), BorderThickness = new Thickness(1),
            Background = Format.Theme("CardBackgroundFillColorDefaultBrush"), BorderBrush = Format.Theme("CardStrokeColorDefaultBrush"),
            Child = text,
        };
    }

    /// <summary>The last resort: a cleared transaction on the file's last date for exactly what is left over. Needs a second press.</summary>
    private async Task BalancingAsync()
    {
        if (_diagnosis is not { Gap: not 0 } d) return;
        if (_confirming != "balancing")
        {
            _confirming = "balancing";
            PrimaryButtonText = "Press again to add it";
            _summary.Text = $"This will add a cleared transaction of {Format.Money(-d.Gap)} on {d.LastDate:d MMM yyyy}, called Balancing adjustment, so that the account agrees with the file. Press the button again to add it, or fix the items below first.";
            return;
        }
        PrimaryButtonText = "Add a balancing transaction";
        var line = new BankLine(0, d.LastDate, "Balancing adjustment", "Added to make the account agree with the bank's file", -d.Gap);
        await _vm.FixAsync(s => s.ApplyBankMatchesAsync(_check.AccountId, Array.Empty<BankClear>(), [line]));
        await RefreshAsync();
    }
}
