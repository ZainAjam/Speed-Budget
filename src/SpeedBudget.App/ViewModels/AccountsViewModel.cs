using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SpeedBudget.Data;

namespace SpeedBudget.App.ViewModels;

/// <summary>One account row. Edits go straight to the entity and are saved by the owning view model.</summary>
public sealed partial class AccountRow(AccountEntity entity, Func<Task> save) : ObservableObject
{
    public AccountEntity Entity { get; } = entity;

    public string Name
    {
        get => Entity.Name;
        set { if (Entity.Name == value) return; Entity.Name = value; Changed(nameof(Name), nameof(Display)); }
    }

    public int TypeIndex
    {
        get => (int)Entity.Type;
        set { if ((int)Entity.Type == value || value < 0) return; Entity.Type = (AccountType)value; Changed(nameof(TypeIndex)); }
    }

    public bool IsOnBudget
    {
        get => Entity.IsOnBudget;
        set { if (Entity.IsOnBudget == value) return; Entity.IsOnBudget = value; Changed(nameof(IsOnBudget), nameof(Kind)); }
    }

    public bool IsClosed
    {
        get => Entity.IsClosed;
        set { if (Entity.IsClosed == value) return; Entity.IsClosed = value; Changed(nameof(IsClosed), nameof(Display), nameof(Kind)); }
    }

    /// <summary>Opening balance in rands. NumberBox hands over NaN when its text is cleared.</summary>
    public double OpeningBalance
    {
        get => (double)AppServices.ToRands(Entity.OpeningBalance);
        set { var cents = AppServices.ToCents(value); if (Entity.OpeningBalance == cents) return; Entity.OpeningBalance = cents; Changed(nameof(OpeningBalance)); }
    }

    public string Display => IsClosed ? $"{Name} (closed)" : Name;

    public string Kind => IsClosed ? "Closed" : IsOnBudget ? "Budget account" : "Tracked account";

    /// <summary>Refreshes the UI after the store closed this account on its own.</summary>
    public void RaiseClosed()
    {
        foreach (var n in new[] { nameof(IsClosed), nameof(Display), nameof(Kind) }) OnPropertyChanged(n);
    }

    private void Changed(params string[] names)
    {
        foreach (var n in names) OnPropertyChanged(n);
        _ = save();
    }
}

public sealed partial class AccountsViewModel : ObservableObject
{
    private readonly BudgetStore _store = AppServices.Store;

    public ObservableCollection<AccountRow> Accounts { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial AccountRow? Selected { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    public bool HasSelection => Selected is not null;

    public bool HasMessage => Message != "";

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    public async Task LoadAsync()
    {
        Accounts.Clear();
        foreach (var a in await _store.GetAccountsAsync()) Accounts.Add(new AccountRow(a, SaveAsync));
        Selected = Accounts.FirstOrDefault();
    }

    public async Task AddAsync()
    {
        var entity = await _store.AddAccountAsync("New account", AccountType.Cheque, isOnBudget: true);
        var row = new AccountRow(entity, SaveAsync);
        Accounts.Add(row);
        Selected = row;
    }

    /// <summary>Returns true if the account was deleted, false if it was closed because it has transactions.</summary>
    public async Task<bool> DeleteSelectedAsync()
    {
        if (Selected is not { } row) return false;
        var deleted = await _store.DeleteOrCloseAccountAsync(row.Entity);
        if (deleted)
        {
            var index = Accounts.IndexOf(row);
            Accounts.Remove(row);
            Selected = Accounts.Count == 0 ? null : Accounts[Math.Min(index, Accounts.Count - 1)];
        }
        else
        {
            row.RaiseClosed();
        }
        return deleted;
    }

    private async Task SaveAsync()
    {
        try { await _store.SaveAsync(); Message = ""; }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
    }
}
