using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SpeedBudget.Core;
using SpeedBudget.Data;
using SortKey = SpeedBudget.Core.SortKey;

namespace SpeedBudget.App.ViewModels;

public sealed record ItemOption(Guid? Id, string Name, ItemKind? Kind);

/// <summary>Id is null for the "All accounts" entry in the filter.</summary>
public sealed record AccountOption(Guid? Id, string Name, bool IsOnBudget);

/// <summary>One transaction in the list. Edits go straight to the entity and are saved by the owner.</summary>
public sealed partial class TransactionRow(TransactionEntity entity, ItemOption item, string accountName, int lineCount, TransactionsViewModel owner)
    : ObservableObject
{
    public TransactionEntity Entity { get; } = entity;
    public string AccountName { get; } = accountName;
    public int LineCount { get; } = lineCount;
    public bool IsTransfer => Entity.TransferPairId is not null;
    public bool IsSplit => LineCount > 1;

    /// <summary>Amount and item are fixed for transfers (the other side depends on them) and split lines (they must add up).</summary>
    public bool CanEditAmount => !IsTransfer && !IsSplit;
    public bool CanSplit => !IsTransfer;

    private ItemOption _item = item;

    public DateTimeOffset? DateOffset
    {
        get => new DateTimeOffset(Entity.Date.ToDateTime(TimeOnly.MinValue));
        set
        {
            if (value is not { } v) return;
            var date = DateOnly.FromDateTime(v.DateTime);
            if (date == Entity.Date) return;
            Entity.Date = date;
            Changed(new[] { nameof(DateOffset), nameof(DateText) }, dateChanged: true);
        }
    }

    public DateTimeOffset? BankDateOffset
    {
        get => Entity.BankDate is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue)) : null;
        set
        {
            DateOnly? date = value is { } v ? DateOnly.FromDateTime(v.DateTime) : null;
            if (date == Entity.BankDate) return;
            Entity.BankDate = date;
            // As in the sheet, entering a bank date clears the transaction.
            if (date is not null) Entity.IsCleared = true;
            Changed(new[] { nameof(BankDateOffset), nameof(IsCleared) });
        }
    }

    public string Payee
    {
        get => Entity.Payee;
        set { if (Entity.Payee == value) return; Entity.Payee = value; Changed(nameof(Payee)); }
    }

    public string Memo
    {
        get => Entity.Memo ?? "";
        set { if ((Entity.Memo ?? "") == value) return; Entity.Memo = value; Changed(nameof(Memo)); }
    }

    public bool IsCleared
    {
        get => Entity.IsCleared;
        set { if (Entity.IsCleared == value) return; Entity.IsCleared = value; Changed(nameof(IsCleared)); }
    }

    public ItemOption Item
    {
        get => _item;
        set
        {
            if (value is null || value == _item) return;
            _item = value;
            Changed(new[] { nameof(Item), nameof(ItemName) }, splitChanged: true);
        }
    }

    public double Outflow
    {
        get => Entity.Amount < 0 ? (double)AppServices.ToRands(-Entity.Amount) : 0;
        set => SetAmount(-AppServices.ToCents(value), nameof(Outflow));
    }

    public double Inflow
    {
        get => Entity.Amount > 0 ? (double)AppServices.ToRands(Entity.Amount) : 0;
        set => SetAmount(AppServices.ToCents(value), nameof(Inflow));
    }

    public string DateText => Entity.Date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    public string ItemName => IsSplit ? $"Split ({LineCount} lines)" : _item.Id is null ? "" : _item.Name;
    public string OutflowText => Entity.Amount < 0 ? AppServices.ToRands(-Entity.Amount).ToString("N2") : "";
    public string InflowText => Entity.Amount > 0 ? AppServices.ToRands(Entity.Amount).ToString("N2") : "";
    public string PayeeText => IsTransfer ? $"⇄ {Entity.Payee}" : Entity.Payee;

    public override string ToString() => $"{DateText} {Entity.Payee}";

    private void SetAmount(long cents, string source)
    {
        // Entering 0 in the box that is not currently in use must not wipe the other side.
        if (cents == 0 && ((source == nameof(Outflow) && Entity.Amount > 0) || (source == nameof(Inflow) && Entity.Amount < 0))) return;
        if (cents == Entity.Amount) return;
        Entity.Amount = cents;
        Changed(new[] { nameof(Outflow), nameof(Inflow), nameof(OutflowText), nameof(InflowText) }, splitChanged: true);
    }

    private void Changed(string name, bool dateChanged = false, bool splitChanged = false) =>
        Changed(new[] { name }, dateChanged, splitChanged);

    private void Changed(string[] names, bool dateChanged = false, bool splitChanged = false)
    {
        foreach (var n in names) OnPropertyChanged(n);
        if (names.Contains(nameof(Payee))) OnPropertyChanged(nameof(PayeeText));
        _ = owner.RowChangedAsync(this, dateChanged, splitChanged);
    }
}

/// <summary>
/// A "Let's start a new month" line in the list, or the heading of the first month, which has no entity and
/// can't be dragged. The line is a point in time: everything after it belongs to the month it starts.
/// </summary>
public sealed partial class MarkerRow(MonthMarkerEntity? entity, Guid monthId, string label, TransactionsViewModel owner)
    : ObservableObject
{
    public MonthMarkerEntity? Entity { get; } = entity;
    public Guid MonthId { get; } = monthId;
    public bool IsFirst => Entity is null;
    public bool CanDrag => !IsFirst;
    public string DateText => Entity is null ? "" : Entity.Date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    private string _label = label;
    public string Label
    {
        get => _label;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || _label == value.Trim()) return;
            _label = value.Trim();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Heading));
            _ = owner.MarkerLabelChangedAsync(this);
        }
    }

    public string Heading => $"{_label} starts here";

    public override string ToString() => Heading;

    private string _subtotals = "";
    public string Subtotals
    {
        get => _subtotals;
        set => SetProperty(ref _subtotals, value);
    }
}

public sealed partial class TransactionsViewModel : ObservableObject
{
    private readonly BudgetStore _store = AppServices.Store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<AccountEntity> _accounts = new();
    private IReadOnlyList<PayeeHint> _hints = Array.Empty<PayeeHint>();
    private bool _loaded;
    private string _lastMonthLabel = "";

    /// <summary>Transaction rows and month lines, interleaved in time order.</summary>
    public ObservableCollection<object> Rows { get; } = new();
    public ObservableCollection<AccountOption> FilterAccounts { get; } = new();
    public ObservableCollection<AccountOption> EntryAccounts { get; } = new();
    public ObservableCollection<ItemOption> ItemOptions { get; } = new();
    public ItemOption NoItem { get; } = new(null, "(no item)", null);

    /// <summary>The selected list entry: a transaction or a month line.</summary>
    [ObservableProperty]
    public partial object? SelectedEntry { get; set; }

    public TransactionRow? Selected => SelectedEntry as TransactionRow;
    public MarkerRow? SelectedMarker => SelectedEntry as MarkerRow;
    public bool HasMarkerSelected => SelectedMarker is not null;
    public bool CanChangeSelectedMarker => SelectedMarker is { IsFirst: false };

    partial void OnSelectedEntryChanged(object? value)
    {
        foreach (var name in new[] { nameof(Selected), nameof(SelectedMarker), nameof(HasSelection), nameof(HasMarkerSelected), nameof(CanChangeSelectedMarker) })
            OnPropertyChanged(name);
    }

    [ObservableProperty]
    public partial AccountOption? FilterAccount { get; set; }

    /// <summary>0 = newest first, 1 = oldest first.</summary>
    [ObservableProperty]
    public partial int SortIndex { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    // Quick-add fields.
    [ObservableProperty] public partial DateTimeOffset NewDate { get; set; } = DateTimeOffset.Now;
    [ObservableProperty] public partial string NewPayee { get; set; } = "";
    [ObservableProperty] public partial string NewMemo { get; set; } = "";
    [ObservableProperty] public partial double NewOutflow { get; set; } = double.NaN;
    [ObservableProperty] public partial double NewInflow { get; set; } = double.NaN;
    [ObservableProperty] public partial ItemOption? NewItem { get; set; }
    [ObservableProperty] public partial AccountOption? NewAccount { get; set; }

    public bool HasSelection => Selected is not null;
    public bool HasMessage => Message != "";

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    partial void OnFilterAccountChanged(AccountOption? value)
    {
        if (!_loaded) return;
        if (value?.Id is { } id) NewAccount = EntryAccounts.FirstOrDefault(a => a.Id == id) ?? NewAccount;
        _ = ReloadAsync();
    }

    partial void OnSortIndexChanged(int value)
    {
        if (_loaded) _ = ReloadAsync();
    }

    public async Task LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _accounts = await _store.GetAccountsAsync();
            var groups = await _store.GetGroupsAsync();
            var items = await _store.GetItemsAsync();
            _hints = await _store.GetPayeeHintsAsync();

            FilterAccounts.Clear();
            EntryAccounts.Clear();
            FilterAccounts.Add(new AccountOption(null, "All accounts", false));
            foreach (var a in _accounts)
            {
                var option = new AccountOption(a.Id, a.IsClosed ? $"{a.Name} (closed)" : a.Name, a.IsOnBudget);
                FilterAccounts.Add(option);
                if (!a.IsClosed) EntryAccounts.Add(option);
            }

            ItemOptions.Clear();
            ItemOptions.Add(NoItem);
            foreach (var g in groups)
                foreach (var i in items.Where(i => i.GroupId == g.Id))
                    ItemOptions.Add(new ItemOption(i.Id, $"{g.Name} › {i.Name}{(i.IsArchived ? " (archived)" : "")}", i.Kind));

            _loaded = false;
            FilterAccount = FilterAccounts[0];
            NewAccount = EntryAccounts.FirstOrDefault();
            NewItem = NoItem;
            _loaded = true;
            await LoadRowsAsync(null);
        }
        finally { _gate.Release(); }
    }

    private async Task ReloadAsync(Guid? select = null)
    {
        await _gate.WaitAsync();
        try { await LoadRowsAsync(select); }
        finally { _gate.Release(); }
    }

    /// <summary>Rebuilds the list. The caller must hold the gate.</summary>
    private async Task LoadRowsAsync(Guid? select)
    {
        var details = await _store.GetTransactionsAsync(FilterAccount?.Id);
        var snapshot = await _store.LoadSnapshotAsync();
        var months = BudgetEngine.Calculate(snapshot);
        _lastMonthLabel = months[^1].Label;

        // Month lines are budget-wide, so they are hidden while looking at a tracked account.
        var showLines = FilterAccount?.Id is null || FilterAccount.IsOnBudget;

        var accountNames = _accounts.ToDictionary(a => a.Id, a => a.Name);
        var itemsById = ItemOptions.Where(o => o.Id is not null).ToDictionary(o => o.Id!.Value);

        // At equal keys a line sorts before a transaction, matching the engine: a transaction level with a line is after it.
        var entries = new List<(SortKey Key, int Rank, Guid Id, object Row)>();
        if (showLines)
        {
            var first = new MarkerRow(null, BudgetEngine.FirstMonthId, snapshot.FirstMonthLabel, this);
            entries.Add((new SortKey(DateOnly.MinValue, int.MinValue), 0, Guid.Empty, first));
            foreach (var m in await _store.GetMarkersAsync())
                entries.Add((new SortKey(m.Date, m.DayOrder), 0, m.Id, new MarkerRow(m, m.Id, m.Label, this)));
        }
        foreach (var d in details)
        {
            var itemId = d.Lines.Count == 1 ? d.Lines[0].BudgetItemId : null;
            var option = itemId is { } id && itemsById.TryGetValue(id, out var found) ? found : NoItem;
            var row = new TransactionRow(d.Transaction, option, accountNames.GetValueOrDefault(d.Transaction.AccountId, "?"), d.Lines.Count, this);
            entries.Add((new SortKey(d.Transaction.Date, d.Transaction.DayOrder), 1, d.Transaction.Id, row));
        }

        var ordered = entries.OrderBy(e => e.Key).ThenBy(e => e.Rank).ThenBy(e => e.Id).Select(e => e.Row).ToList();
        if (SortIndex == 0) ordered.Reverse();

        Rows.Clear();
        object? toSelect = null;
        foreach (var row in ordered)
        {
            if (row is MarkerRow marker) marker.Subtotals = SubtotalsFor(months, marker.MonthId);
            Rows.Add(row);
            var id = row switch { TransactionRow t => t.Entity.Id, MarkerRow { Entity: { } m } => m.Id, _ => Guid.Empty };
            if (select is { } wanted && id == wanted) toSelect = row;
        }
        SelectedEntry = toSelect;
    }

    private static string SubtotalsFor(IReadOnlyList<MonthSummary> months, Guid monthId)
    {
        var month = months.First(m => m.MonthId == monthId);
        var spent = -month.Items.Sum(i => i.Activity);
        string Money(long cents) => AppServices.ToRands(cents).ToString("N2");
        return $"Income {Money(month.Income)}   ·   Spent {Money(spent)}   ·   Available to budget {Money(month.Available)}";
    }

    /// <summary>Recalculates the totals shown on the month lines after an edit that does not reload the list.</summary>
    private async Task RefreshSubtotalsAsync()
    {
        var months = BudgetEngine.Calculate(await _store.LoadSnapshotAsync());
        foreach (var marker in Rows.OfType<MarkerRow>()) marker.Subtotals = SubtotalsFor(months, marker.MonthId);
    }

    public async Task RowChangedAsync(TransactionRow row, bool dateChanged, bool splitChanged)
    {
        await _gate.WaitAsync();
        try
        {
            if (dateChanged) row.Entity.DayOrder = await _store.NextDayOrderAsync(row.Entity.Date);
            if (splitChanged) await _store.SetSingleSplitAsync(row.Entity.Id, row.Item.Id, row.Entity.Amount);
            else await _store.SaveAsync();
            Message = "";
            if (dateChanged) await LoadRowsAsync(row.Entity.Id);
            else if (splitChanged) await RefreshSubtotalsAsync();
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    public async Task MarkerLabelChangedAsync(MarkerRow marker)
    {
        await _gate.WaitAsync();
        try
        {
            if (marker.Entity is null) await _store.SetFirstMonthLabelAsync(marker.Label);
            else
            {
                marker.Entity.Label = marker.Label;
                await _store.SaveAsync();
            }
            Message = "";
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>The label to suggest for a month starting on the given date: the month after the latest one.</summary>
    public string SuggestMonthLabel(DateTimeOffset date) => MonthLabels.Next(_lastMonthLabel, DateOnly.FromDateTime(date.DateTime));

    public async Task AddMonthAsync(string label, DateTimeOffset date, bool copyPreviousBudget)
    {
        await _gate.WaitAsync();
        try
        {
            var marker = await _store.AddMarkerAsync(label.Trim(), DateOnly.FromDateTime(date.DateTime), copyPreviousBudget);
            Message = "";
            await LoadRowsAsync(marker.Id);
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Called after a month line was dragged to a new place in the list. Works out the line's new date and order from
    /// the transactions now above and below it, then reloads so the list shows where it really landed.
    /// </summary>
    public async Task MarkerDroppedAsync(MarkerRow marker)
    {
        await _gate.WaitAsync();
        try
        {
            if (marker.Entity is { } entity)
            {
                var index = Rows.IndexOf(marker);
                TransactionRow? above = null, below = null;
                for (var i = index - 1; i >= 0 && above is null; i--) above = Rows[i] as TransactionRow;
                for (var i = index + 1; i < Rows.Count && below is null; i++) below = Rows[i] as TransactionRow;

                // Newest-first lists run backwards in time, so "above" is later.
                var newestFirst = SortIndex == 0;
                var earlier = newestFirst ? below : above;
                var later = newestFirst ? above : below;
                static SortKey? KeyOf(TransactionRow? t) => t is null ? null : new SortKey(t.Entity.Date, t.Entity.DayOrder);

                if (MarkerPlacement.Place(KeyOf(earlier), KeyOf(later)) is { } key)
                    Message = await _store.MoveMarkerAsync(entity, key) ? "" : "A month line can't be dragged past another month line.";
            }
            await LoadRowsAsync(marker.Entity?.Id);
        }
        catch (Exception ex) { Message = $"Could not move the line: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>Moves a month line one transaction up or down the list, for keyboard use.</summary>
    public async Task MoveMarkerByRowAsync(MarkerRow marker, int delta)
    {
        var index = Rows.IndexOf(marker);
        var target = index + delta;
        if (marker.IsFirst || index < 0 || target < 0 || target >= Rows.Count || Rows[target] is MarkerRow) return;
        Rows.Move(index, target);
        await MarkerDroppedAsync(marker);
    }

    public async Task DeleteSelectedMarkerAsync()
    {
        if (SelectedMarker is not { Entity: { } entity }) return;
        await _gate.WaitAsync();
        try
        {
            await _store.DeleteMarkerAsync(entity);
            Message = "";
            await LoadRowsAsync(null);
        }
        catch (Exception ex) { Message = $"Could not delete: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    public IReadOnlyList<string> SuggestPayees(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        return _hints.Where(h => h.Payee.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(h => h.Payee).Take(8).ToList();
    }

    /// <summary>Fills in the item and account last used with this payee, if those are still unset.</summary>
    public void ApplyHint(string payee)
    {
        var hint = _hints.FirstOrDefault(h => string.Equals(h.Payee, payee.Trim(), StringComparison.OrdinalIgnoreCase));
        if (hint is null) return;
        if (NewItem?.Id is null && hint.BudgetItemId is { } itemId)
            NewItem = ItemOptions.FirstOrDefault(o => o.Id == itemId) ?? NoItem;
        if (FilterAccount?.Id is null)
            NewAccount = EntryAccounts.FirstOrDefault(a => a.Id == hint.AccountId) ?? NewAccount;
    }

    /// <summary>Returns true when the transaction was added.</summary>
    public async Task<bool> AddAsync()
    {
        var accountOption = NewAccount;
        if (accountOption?.Id is not { } accountId) { Message = "Choose an account."; return false; }

        var outflow = AppServices.ToCents(NewOutflow);
        var inflow = AppServices.ToCents(NewInflow);
        if ((outflow == 0) == (inflow == 0)) { Message = "Enter either an outflow or an inflow."; return false; }
        if (outflow < 0 || inflow < 0) { Message = "Amounts must be positive. Use outflow or inflow to say which way the money went."; return false; }
        if (accountOption.IsOnBudget && NewItem?.Id is null) { Message = "Choose a budget item for a budget account."; return false; }

        await _gate.WaitAsync();
        try
        {
            var date = DateOnly.FromDateTime(NewDate.DateTime);
            var amount = inflow - outflow;
            var tx = await _store.AddTransactionAsync(
                new TransactionEntity
                {
                    AccountId = accountId, Date = date, DayOrder = await _store.NextDayOrderAsync(date),
                    Payee = NewPayee.Trim(), Memo = string.IsNullOrWhiteSpace(NewMemo) ? null : NewMemo.Trim(), Amount = amount,
                },
                new[] { new SplitLineEntity { BudgetItemId = NewItem?.Id, Amount = amount } });

            _hints = await _store.GetPayeeHintsAsync();
            NewPayee = "";
            NewMemo = "";
            NewOutflow = double.NaN;
            NewInflow = double.NaN;
            Message = "";
            await LoadRowsAsync(tx.Id);
            return true;
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; return false; }
        finally { _gate.Release(); }
    }

    /// <summary>Budget accounts need an item on every line; tracked accounts may leave lines unassigned.</summary>
    public bool RequiresItem(TransactionRow row) => _accounts.FirstOrDefault(a => a.Id == row.Entity.AccountId)?.IsOnBudget ?? false;

    public Task<List<SplitLineEntity>> GetLinesAsync(TransactionRow row) => _store.GetSplitLinesAsync(row.Entity.Id);

    /// <summary>Returns an error sentence, or null when the split was saved.</summary>
    public async Task<string?> SaveSplitsAsync(TransactionRow row, IReadOnlyList<(Guid? ItemId, long Amount, string? Memo)> lines)
    {
        await _gate.WaitAsync();
        try
        {
            await _store.SetSplitsAsync(row.Entity.Id, lines);
            await LoadRowsAsync(row.Entity.Id);
            return null;
        }
        catch (ArgumentException ex) { return ex.Message; }
        catch (Exception ex) { return $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    public async Task DeleteSelectedAsync()
    {
        if (Selected is not { } row) return;
        await _gate.WaitAsync();
        try
        {
            await _store.DeleteTransactionAsync(row.Entity);
            await LoadRowsAsync(null);
        }
        catch (Exception ex) { Message = $"Could not delete: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>Returns an error sentence, or null when the transfer was saved.</summary>
    public async Task<string?> AddTransferAsync(AccountOption? from, AccountOption? to, DateTimeOffset date, double amount, string memo, ItemOption? item)
    {
        if (from?.Id is not { } fromId || to?.Id is not { } toId) return "Choose both accounts.";
        await _gate.WaitAsync();
        try
        {
            await _store.AddTransferAsync(fromId, toId, DateOnly.FromDateTime(date.DateTime), AppServices.ToCents(amount),
                string.IsNullOrWhiteSpace(memo) ? null : memo.Trim(), item?.Id);
            await LoadRowsAsync(null);
            return null;
        }
        catch (ArgumentException ex) { return ex.Message; }
        catch (Exception ex) { return $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }
}
