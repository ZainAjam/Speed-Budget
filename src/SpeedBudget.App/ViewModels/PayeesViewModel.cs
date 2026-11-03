using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SpeedBudget.Data;

namespace SpeedBudget.App.ViewModels;

/// <summary>One payee in the list. Renaming goes through the owner, which also renames the payee on its transactions.</summary>
public sealed partial class PayeeRow(PayeeEntity entity, int uses, PayeesViewModel owner) : ObservableObject
{
    public PayeeEntity Entity { get; } = entity;
    public int Uses { get; set; } = uses;

    public string Name
    {
        get => Entity.Name;
        set
        {
            if (Entity.Name == value) return;
            _ = owner.RenameAsync(this, value);
        }
    }

    public string UsageText => Uses switch { 0 => "Not used yet", 1 => "Used on 1 transaction", _ => $"Used on {Uses} transactions" };

    /// <summary>Redraws the name and usage, for when a rename was refused and the box must go back to the old name.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(UsageText));
    }

    public override string ToString() => Name;
}

public sealed partial class PayeesViewModel : ObservableObject
{
    private readonly BudgetStore _store = AppServices.Store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<PayeeRow> _all = new();

    public ObservableCollection<PayeeRow> Rows { get; } = new();

    [ObservableProperty] public partial PayeeRow? Selected { get; set; }
    [ObservableProperty] public partial string Search { get; set; } = "";
    [ObservableProperty] public partial string Message { get; set; } = "";

    public bool HasSelection => Selected is not null;
    public bool HasMessage => Message != "";

    partial void OnSelectedChanged(PayeeRow? value) => OnPropertyChanged(nameof(HasSelection));

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    partial void OnSearchChanged(string value) => ApplyFilter(Selected?.Entity.Id);

    public async Task LoadAsync(Guid? select = null, string? selectName = null)
    {
        await _gate.WaitAsync();
        try
        {
            // Opened from a transaction: find the counterparty by the name that transaction carries.
            if (select is null && !string.IsNullOrWhiteSpace(selectName))
            {
                var wanted = selectName.Trim();
                select = (await _store.GetPayeesAsync()).FirstOrDefault(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase))?.Id;
            }
            await ReloadAsync(select);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Rebuilds the list from the database. The caller must hold the gate.</summary>
    private async Task ReloadAsync(Guid? select)
    {
        await _store.EnsurePayeesFromTransactionsAsync();
        var usage = await _store.GetPayeeUsageAsync();
        _all = (await _store.GetPayeesAsync())
            .Select(p => new PayeeRow(p, usage.GetValueOrDefault(p.Name.ToLower()), this)).ToList();
        ApplyFilter(select);
    }

    private void ApplyFilter(Guid? select)
    {
        var words = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Rows.Clear();
        foreach (var row in _all.Where(r => words.All(w => r.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            Rows.Add(row);
        Selected = Rows.FirstOrDefault(r => r.Entity.Id == select);
    }

    public async Task AddAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var names = (await _store.GetPayeesAsync()).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var name = "New counterparty";
            for (var n = 2; names.Contains(name); n++) name = $"New counterparty {n}";
            var payee = await _store.EnsurePayeeAsync(name);
            Search = "";
            Message = "";
            await ReloadAsync(payee.Id);
        }
        catch (Exception ex) { Message = $"Could not add the counterparty: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    public async Task RenameAsync(PayeeRow row, string newName)
    {
        await _gate.WaitAsync();
        try
        {
            var error = await _store.RenamePayeeAsync(row.Entity, newName);
            Message = error ?? "";
            row.Refresh();
            if (error is null) ApplyFilter(row.Entity.Id);
        }
        catch (Exception ex) { Message = $"Could not rename: {ex.Message}"; row.Refresh(); }
        finally { _gate.Release(); }
    }

    /// <summary>Deletes the selected payee unless transactions use it. Returns true when deleted.</summary>
    public async Task<bool> DeleteSelectedAsync()
    {
        if (Selected is not { } row) return false;
        await _gate.WaitAsync();
        try
        {
            if (!await _store.DeletePayeeAsync(row.Entity))
            {
                Message = $"{row.Name} is used by {row.Uses} transaction(s), so it can't be deleted. Rename it, or change those transactions first.";
                return false;
            }
            Message = "";
            await ReloadAsync(null);
            return true;
        }
        catch (Exception ex) { Message = $"Could not delete: {ex.Message}"; return false; }
        finally { _gate.Release(); }
    }
}
