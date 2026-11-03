using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SpeedBudget.Core;
using SpeedBudget.Data;

namespace SpeedBudget.App.ViewModels;

/// <summary>A transaction the bank has not processed yet. Ticking it clears it and takes it off the list.</summary>
public sealed partial class UnclearedRow(TransactionEntity entity, ReconcileViewModel owner) : ObservableObject
{
    public TransactionEntity Entity { get; } = entity;
    public string DateText => Entity.Date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    public string Payee => Entity.Payee;
    public string AmountText => Format.Money(Entity.Amount);

    public override string ToString() => $"{DateText} {Payee}";

    public bool IsCleared
    {
        get => Entity.IsCleared;
        set
        {
            if (Entity.IsCleared == value) return;
            Entity.IsCleared = value;
            OnPropertyChanged();
            _ = owner.ClearedChangedAsync(this);
        }
    }
}

public sealed partial class ReconcileViewModel : ObservableObject
{
    private readonly BudgetStore _store = AppServices.Store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<AccountEntity> _accounts = new();
    private bool _loading;

    public ObservableCollection<AccountOption> Accounts { get; } = new();
    public ObservableCollection<UnclearedRow> Uncleared { get; } = new();

    [ObservableProperty] public partial AccountOption? SelectedAccount { get; set; }
    [ObservableProperty] public partial double Statement { get; set; } = double.NaN;
    [ObservableProperty] public partial string BookText { get; set; } = "";
    [ObservableProperty] public partial string ClearedText { get; set; } = "";
    [ObservableProperty] public partial string UnclearedText { get; set; } = "";
    [ObservableProperty] public partial string DifferenceText { get; set; } = "";
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial string AvailableText { get; set; } = "";
    [ObservableProperty] public partial bool HasAccount { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = "";

    public bool HasMessage => Message != "";

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    partial void OnSelectedAccountChanged(AccountOption? value)
    {
        if (!_loading) _ = LoadAccountAsync();
    }

    partial void OnStatementChanged(double value)
    {
        if (!_loading) _ = StatementChangedAsync(value);
    }

    public async Task LoadAsync()
    {
        _accounts = (await _store.GetAccountsAsync()).Where(a => !a.IsClosed).ToList();
        _loading = true;
        Accounts.Clear();
        foreach (var a in _accounts) Accounts.Add(new AccountOption(a.Id, a.Name, a.IsOnBudget));
        SelectedAccount = Accounts.FirstOrDefault();
        _loading = false;
        HasAccount = SelectedAccount is not null;
        await LoadAccountAsync();
    }

    private async Task LoadAccountAsync()
    {
        if (SelectedAccount?.Id is not { } id) return;
        await _gate.WaitAsync();
        try
        {
            var statement = await _store.GetStatementBalanceAsync(id);
            _loading = true;
            Statement = statement is { } s ? (double)AppServices.ToRands(s) : double.NaN;
            _loading = false;
            await RecalculateAsync(id, statement);
        }
        catch (Exception ex) { Message = $"Could not load: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    private async Task StatementChangedAsync(double rands)
    {
        if (SelectedAccount?.Id is not { } id) return;
        await _gate.WaitAsync();
        try
        {
            long? cents = double.IsNaN(rands) ? null : AppServices.ToCents(rands);
            await _store.SetStatementBalanceAsync(id, cents);
            await RecalculateAsync(id, cents);
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>Rebuilds the figures and the list of uncleared transactions. The caller must hold the gate.</summary>
    private async Task RecalculateAsync(Guid accountId, long? statement)
    {
        var account = _accounts.Single(a => a.Id == accountId);
        var transactions = (await _store.GetTransactionsAsync(accountId)).Select(d => d.Transaction).ToList();
        var result = Reconciliation.Calculate(account.OpeningBalance,
            transactions.Select(t => new ReconcileLine(t.Amount, t.IsCleared)), statement);

        BookText = Format.Money(result.BookBalance);
        ClearedText = Format.Money(result.ClearedBalance);
        UnclearedText = Format.Money(result.UnclearedTotal);
        DifferenceText = result.Difference is { } d ? Format.Money(d) : "–";
        AvailableText = result.AvailableToSpend is { } a ? Format.Money(a) : "–";
        StatusText = result.Difference is null ? "Enter the balance from your bank statement."
            : result.IsReconciled ? "Reconciled: your cleared transactions match the statement."
            : $"Not reconciled: the statement is {Format.Money(Math.Abs(result.Difference.Value))} {(result.Difference > 0 ? "more" : "less")} than your cleared transactions.";

        Uncleared.Clear();
        foreach (var t in transactions.Where(t => !t.IsCleared).OrderByDescending(t => t.Date).ThenByDescending(t => t.DayOrder))
            Uncleared.Add(new UnclearedRow(t, this));
        Message = "";
    }

    public async Task ClearedChangedAsync(UnclearedRow row)
    {
        if (SelectedAccount?.Id is not { } id) return;
        await _gate.WaitAsync();
        try
        {
            await _store.SaveAsync();
            await RecalculateAsync(id, long.TryParse(await _store.GetSettingAsync($"statement:{id}"), out var s) ? s : null);
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }
}
