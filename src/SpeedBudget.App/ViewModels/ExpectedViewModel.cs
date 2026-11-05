using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using SpeedBudget.Data;

namespace SpeedBudget.App.ViewModels;

/// <summary>One expected transaction in the list under the Expected transactions flyout.</summary>
public sealed class ExpectedRow(ExpectedTransactionEntity entity, ExpectedViewModel owner)
{
    public ExpectedTransactionEntity Entity { get; } = entity;

    public bool ShowItem => owner.ShowItem;
    public string PayeeText => Entity.Payee == "" ? "(no counterparty)" : owner.Owner.WithIcon(Entity.Payee);
    public string MemoText => Entity.Memo ?? "";
    public string ItemText => Entity.BudgetItemId is { } id ? owner.Owner.ItemOptions.FirstOrDefault(o => o.Id == id)?.Label ?? "" : "";
    public string OutflowText => Entity.Amount < 0 ? Format.Money(-Entity.Amount) : "";
    public string InflowText => Entity.Amount > 0 ? Format.Money(Entity.Amount) : "";
    public Brush OutflowBrush => Format.Theme("SystemFillColorCriticalBrush");
    public Brush InflowBrush => Format.Theme("SystemFillColorSuccessBrush");
    public string DateText => Entity.ExpectedDate is { } d ? d.ToString("d MMM yyyy") : "No date";
    public double DateOpacity => Entity.ExpectedDate is null ? 0.55 : 0.9;
}

/// <summary>
/// The Expected transactions flyout of an account: things that are coming but have not happened yet. When one happens it is
/// recorded in the transactions table on the date it happened, and leaves this list.
/// </summary>
public sealed partial class ExpectedViewModel(TransactionsViewModel owner) : ObservableObject
{
    private readonly BudgetStore _store = AppServices.Store;
    private ExpectedRow? _editing;

    public TransactionsViewModel Owner { get; } = owner;

    public ObservableCollection<ExpectedRow> Rows { get; } = new();

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = "";

    // The boxes at the top of the list, for adding an expected transaction or changing one.
    [ObservableProperty] public partial string FormPayee { get; set; } = "";
    [ObservableProperty] public partial string FormMemo { get; set; } = "";
    [ObservableProperty] public partial double FormOutflow { get; set; } = double.NaN;
    [ObservableProperty] public partial double FormInflow { get; set; } = double.NaN;
    [ObservableProperty] public partial ItemOption? FormItem { get; set; }
    [ObservableProperty] public partial DateTimeOffset? FormDate { get; set; }
    [ObservableProperty] public partial string SaveText { get; set; } = "Add";
    [ObservableProperty] public partial string CountText { get; set; } = "Expected transactions";

    public bool HasMessage => Message != "";
    public bool CanCancelEdit => _editing is not null;

    /// <summary>Expected transactions belong to one account, so there is no flyout when looking at all accounts.</summary>
    public bool IsAvailable => Owner.AccountId is not null;

    /// <summary>Tracked accounts have no budget, so their expected transactions have no budget item.</summary>
    public bool ShowItem => Owner.AccountId is { } id && Owner.IsOnBudget(id);

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    /// <summary>The bar along the bottom that opens the flyout; it gives way to the flyout while that is open.</summary>
    public bool ShowBar => IsAvailable && !IsOpen;

    partial void OnIsOpenChanged(bool value) => OnPropertyChanged(nameof(ShowBar));

    private void Raise()
    {
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(ShowBar));
        OnPropertyChanged(nameof(ShowItem));
        OnPropertyChanged(nameof(CanCancelEdit));
    }

    public async Task LoadAsync()
    {
        Rows.Clear();
        if (Owner.AccountId is { } id)
        {
            await Owner.Gate.WaitAsync();
            try { foreach (var e in await _store.GetExpectedAsync(id)) Rows.Add(new ExpectedRow(e, this)); }
            catch (Exception ex) { Message = $"Could not load the expected transactions: {ex.Message}"; }
            finally { Owner.Gate.Release(); }
        }
        FormItem ??= Owner.NoItem;
        UpdateCount();
        Raise();
    }

    private void UpdateCount() => CountText = Rows.Count == 0 ? "Expected transactions" : $"Expected transactions ({Rows.Count})";

    private void ClearForm()
    {
        _editing = null;
        FormPayee = "";
        FormMemo = "";
        FormOutflow = double.NaN;
        FormInflow = double.NaN;
        FormItem = Owner.NoItem;
        FormDate = null;
        SaveText = "Add";
        Raise();
    }

    /// <summary>Puts an expected transaction's details in the boxes, to change them.</summary>
    public void BeginEdit(ExpectedRow row)
    {
        var e = row.Entity;
        _editing = row;
        FormPayee = e.Payee;
        FormMemo = e.Memo ?? "";
        FormOutflow = e.Amount < 0 ? (double)AppServices.ToRands(-e.Amount) : double.NaN;
        FormInflow = e.Amount > 0 ? (double)AppServices.ToRands(e.Amount) : double.NaN;
        FormItem = e.BudgetItemId is { } id ? Owner.ItemOptions.FirstOrDefault(o => o.Id == id) ?? Owner.NoItem : Owner.NoItem;
        FormDate = e.ExpectedDate is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue)) : null;
        SaveText = "Save";
        Message = "";
        Raise();
    }

    public void CancelEdit() => ClearForm();

    /// <summary>Adds the transaction in the boxes to the list, or saves the changes to the one being edited.</summary>
    public async Task SaveFormAsync()
    {
        if (Owner.AccountId is not { } accountId) return;
        var cents = !double.IsNaN(FormOutflow) && AppServices.ToCents(FormOutflow) > 0 ? -AppServices.ToCents(FormOutflow)
            : !double.IsNaN(FormInflow) ? AppServices.ToCents(FormInflow) : 0;
        if (cents == 0) { Message = "Enter the amount, as money out or money in."; return; }
        if (FormPayee.Trim() == "" && FormMemo.Trim() == "") { Message = "Say who it is with, or add a memo, so you can tell what it is."; return; }

        DateOnly? date = FormDate is { } d ? DateOnly.FromDateTime(d.DateTime) : null;
        Guid? itemId = ShowItem ? FormItem?.Id : null;

        await Owner.Gate.WaitAsync();
        try
        {
            if (_editing is { } row)
            {
                var e = row.Entity;
                e.Payee = FormPayee; e.Memo = FormMemo; e.Amount = cents; e.BudgetItemId = itemId; e.ExpectedDate = date;
                await _store.UpdateExpectedAsync(e);
            }
            else
            {
                await _store.AddExpectedAsync(new ExpectedTransactionEntity
                {
                    AccountId = accountId, Payee = FormPayee, Memo = FormMemo, Amount = cents, BudgetItemId = itemId, ExpectedDate = date,
                });
            }
            if (FormPayee.Trim() != "") await _store.EnsurePayeeAsync(FormPayee);
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; return; }
        finally { Owner.Gate.Release(); }

        Message = "";
        ClearForm();
        await LoadAsync();
    }

    public async Task DeleteAsync(ExpectedRow row)
    {
        await Owner.Gate.WaitAsync();
        try { await _store.DeleteExpectedAsync(row.Entity); }
        catch (Exception ex) { Message = $"Could not delete: {ex.Message}"; return; }
        finally { Owner.Gate.Release(); }
        if (_editing == row) ClearForm();
        Message = "";
        await LoadAsync();
    }

    /// <summary>It happened: add it to the transactions table on this date, and take it off the expected list.</summary>
    public async Task RecordAsync(ExpectedRow row, DateOnly date)
    {
        if (!await Owner.RecordExpectedAsync(row.Entity, date)) return;
        if (_editing == row) ClearForm();
        Message = "";
        await LoadAsync();
    }
}
