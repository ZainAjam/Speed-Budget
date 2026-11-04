using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SpeedBudget.Core;
using SpeedBudget.Data;
using SortKey = SpeedBudget.Core.SortKey;

namespace SpeedBudget.App.ViewModels;

/// <summary>
/// A budget item as offered in a drop-down. Name is the full label (group, emoji, item). Balance is what is left on the
/// item this month, kept up to date in place so an open list never shows a stale figure.
/// </summary>
public sealed class ItemOption(Guid? id, string name, ItemKind? kind, Guid? groupId = null, string shortName = "")
{
    public Guid? Id { get; } = id;
    public string Name { get; } = name;
    public ItemKind? Kind { get; } = kind;
    public Guid? GroupId { get; } = groupId;

    /// <summary>The item's own name, without its group or emoji, for telling whether typed text already exists.</summary>
    public string ShortName { get; } = shortName;

    /// <summary>True for the "add a new item" row at the top of a search, which is not a real item.</summary>
    public bool IsCreateRow { get; init; }

    /// <summary>True for the "split between several items" row at the top of the list, which is not a real item.</summary>
    public bool IsSplitRow { get; init; }

    public long? Balance { get; set; }
    public string BalanceText => Balance is { } b ? Format.Money(b) : "";
    public Microsoft.UI.Xaml.Media.Brush BalanceBrush => Balance is { } b ? Format.Signed(b) : Format.Theme("TextFillColorPrimaryBrush");
}

/// <summary>Id is null for "all accounts".</summary>
public sealed record AccountOption(Guid? Id, string Name, bool IsOnBudget);

/// <summary>One transaction in the list. Edits go straight to the entity and are saved by the owner.</summary>
public sealed partial class TransactionRow(TransactionEntity entity, ItemOption item, string accountName, int lineCount,
    TransactionsViewModel owner) : ObservableObject
{
    /// <summary>Tracked accounts sit outside the budget, so their transactions have no budget item to show or edit.</summary>
    public bool AccountOnBudget => owner.IsOnBudget(Entity.AccountId);

    /// <summary>A new row that has not been saved yet. It is added to the list by the Add buttons and saved like any other edit.</summary>
    public bool IsNew { get; init; }

    public ColumnLayout Layout => ColumnLayout.Current;

    /// <summary>The lists the row's drop-downs choose from.</summary>
    public ObservableCollection<string> Payees => owner.Payees;
    public ObservableCollection<ItemOption> ItemOptions => owner.ItemOptions;
    public TransactionEntity Entity { get; } = entity;
    public string AccountName { get; } = accountName;

    /// <summary>For a transfer: the account on its other side, and whether that account is part of the budget.</summary>
    public Guid? OtherAccountId { get; set; }
    /// <summary>Whether this row counts in the budget: a budget account's transaction, or a transfer with a budget account on either side.</summary>
    public bool TouchesBudget => AccountOnBudget || (IsTransfer && OtherOnBudget);

    public bool OtherOnBudget => OtherAccountId is { } id && owner.IsOnBudget(id);

    // A new row's split lines are kept here until the row is saved.
    private IReadOnlyList<(Guid? ItemId, long Amount, string? Memo)>? _draftSplits;
    public IReadOnlyList<(Guid? ItemId, long Amount, string? Memo)>? DraftSplits => _draftSplits;

    /// <summary>Sets the split lines of a new row. A single line is just an item.</summary>
    public void SetDraftSplits(IReadOnlyList<(Guid? ItemId, long Amount, string? Memo)> lines)
    {
        _draftSplits = lines.Count > 1 ? lines : null;
        if (lines.Count == 1) _item = owner.ItemOptions.FirstOrDefault(o => o.Id == lines[0].ItemId) ?? owner.NoItem;
        foreach (var n in new[] { nameof(LineCount), nameof(IsSplit), nameof(ItemName), nameof(Item), nameof(CanEditAmount), nameof(ShowAmountEditor),
                                  nameof(ShowAmountText), nameof(ShowItemEditor), nameof(ShowSplitButton), nameof(ShowItemText) })
            OnPropertyChanged(n);
    }

    /// <summary>The payee's usual item (and account, on the all-accounts page) fill in a new row.</summary>
    public void ApplyHint(ItemOption? hintItem, Guid? hintAccount)
    {
        if (hintItem is not null && _item.Id is null && !IsSplit) { _item = hintItem; OnPropertyChanged(nameof(Item)); OnPropertyChanged(nameof(ItemName)); }
        if (hintAccount is { } account && account != Entity.AccountId && Accounts.Any(a => a.Id == account))
        {
            Entity.AccountId = account;
            RaiseAccountDependent();
        }
    }

    /// <summary>What depends on the account (and the other account of a transfer): which boxes the row offers.</summary>
    private void RaiseAccountDependent()
    {
        foreach (var n in new[] { nameof(AccountChoice), nameof(OtherAccountChoice), nameof(AccountOnBudget), nameof(OtherOnBudget), nameof(ShowItemEditor),
                                  nameof(ShowItemText), nameof(ItemName) })
            OnPropertyChanged(n);
        owner.SyncColumnLayout();
    }

    public ObservableCollection<AccountOption> Accounts => owner.EntryAccounts;

    /// <summary>The account this row's transaction is in, for the Account box.</summary>
    public AccountOption? AccountChoice
    {
        get => Accounts.FirstOrDefault(a => a.Id == Entity.AccountId);
        set
        {
            if (value?.Id is not { } id || id == Entity.AccountId) return;
            Entity.AccountId = id;
            RaiseAccountDependent();
            Changed(new[] { nameof(AccountChoice) }, structural: true);
        }
    }

    /// <summary>For a transfer: the account on the other side, for the Counterparty box.</summary>
    public AccountOption? OtherAccountChoice
    {
        get => Accounts.FirstOrDefault(a => a.Id == OtherAccountId);
        set
        {
            if (value?.Id is not { } id || id == OtherAccountId) return;
            OtherAccountId = id;
            RaiseAccountDependent();
            Changed(new[] { nameof(OtherAccountChoice) }, structural: true);
        }
    }
    public int LineCount => _draftSplits?.Count ?? lineCount;
    public bool IsTransfer => Entity.TransferPairId is not null;
    public bool IsSplit => LineCount > 1;

    /// <summary>The selected row shows editing boxes in place of its text.</summary>
    [ObservableProperty] public partial bool IsEditing { get; set; }

    // ---- Editing session: changes are held until committed, and can be thrown away ----

    private sealed record Snapshot(DateOnly Date, DateOnly? BankDate, string Payee, string? Memo, bool IsCleared, long Amount,
        Guid AccountId, ItemOption Item, Guid? OtherAccountId);

    private Snapshot? _before;
    private bool _dirtyDate, _dirtySplit, _dirtyStructural;

    private bool _dirty;

    /// <summary>The row has edits that have not been saved yet. A new row always has.</summary>
    public bool IsDirty => !_discarded && (IsNew || _dirty);

    private void TakeSnapshot()
    {
        _before = new Snapshot(Entity.Date, Entity.BankDate, Entity.Payee, Entity.Memo, Entity.IsCleared, Entity.Amount,
            Entity.AccountId, _item, OtherAccountId);
        _dirty = _dirtyDate = _dirtySplit = _dirtyStructural = false;
    }

    /// <summary>Saves the edits made since the row went into edit mode, if there are any. False when a new row could not be saved yet.</summary>
    public async Task<bool> CommitAsync()
    {
        if (IsNew) return _discarded || await owner.CommitNewAsync(this);
        if (!_dirty) return true;
        // Nothing in the budget can be dated before the budget starts; the user may move the start back, or the edit is dropped.
        if ((_dirtyDate || _dirtyStructural) && TouchesBudget && !await owner.EnsureWithinBudgetAsync(Entity.Date))
        {
            Cancel();
            return true;
        }
        var (date, split, structural) = (_dirtyDate, _dirtySplit, _dirtyStructural);
        TakeSnapshot();   // what is now saved is the new "before"
        await owner.RowChangedAsync(this, date, split, structural);
        return true;
    }

    /// <summary>Puts back the values the row had before it went into edit mode. Nothing was saved, so nothing is undone in the data.</summary>
    public void Cancel()
    {
        if (_before is not { } b) return;
        Entity.Date = b.Date;
        Entity.BankDate = b.BankDate;
        Entity.Payee = b.Payee;
        Entity.Memo = b.Memo;
        Entity.IsCleared = b.IsCleared;
        Entity.Amount = b.Amount;
        Entity.AccountId = b.AccountId;
        _item = b.Item;
        OtherAccountId = b.OtherAccountId;
        _dirty = _dirtyDate = _dirtySplit = _dirtyStructural = false;
        OnPropertyChanged(string.Empty);   // every box shows its old value again
    }

    partial void OnIsEditingChanged(bool value)
    {
        if (value) TakeSnapshot();
        foreach (var n in new[] { nameof(ShowPayeeEditor), nameof(ShowTransferEditor), nameof(ShowPayeeText), nameof(ShowAmountEditor), nameof(ShowAmountText),
                                  nameof(ShowItemEditor), nameof(ShowSplitButton), nameof(ShowItemText), nameof(NotEditing) })
            OnPropertyChanged(n);
    }

    public bool NotEditing => !IsEditing;

    // Which cells show a box and which show text while the row is being edited. A transfer's counterparty is the account on
    // its other side; it has a budget item only when exactly one of its two accounts is in the budget.
    public bool ShowPayeeEditor => IsEditing && !IsTransfer;
    public bool ShowTransferEditor => IsEditing && IsTransfer;
    public bool ShowPayeeText => !IsEditing;
    public bool ShowAmountEditor => IsEditing && CanEditAmount;
    public bool ShowAmountText => !ShowAmountEditor;
    public bool ShowItemEditor => IsEditing && CanEditAmount
        && (IsTransfer ? (IsNew ? AccountOnBudget != OtherOnBudget : AccountOnBudget && !OtherOnBudget) : AccountOnBudget);
    public bool ShowSplitButton => IsEditing && IsSplit;
    public bool ShowItemText => !ShowItemEditor && !ShowSplitButton;

    /// <summary>A split's amount is fixed by its lines, which must add up. A transfer's amount is shared by its two sides.</summary>
    public bool CanEditAmount => !IsSplit;
    public bool CanSplit => !IsTransfer;

    /// <summary>The row was dropped, or saved under another row, so it must not be saved again.</summary>
    private bool _discarded;
    public void MarkDiscarded() => _discarded = true;

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
            Changed(new[] { nameof(BankDateOffset), nameof(BankDateText), nameof(IsCleared) });
        }
    }

    public string Payee
    {
        get => Entity.Payee;
        set
        {
            if (Entity.Payee == value) return;
            Entity.Payee = value;
            Changed(nameof(Payee));
            if (IsNew && !IsTransfer && !string.IsNullOrWhiteSpace(value)) owner.ApplyHint(this);
        }
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
        get => Entity.Amount < 0 ? (double)AppServices.ToRands(-Entity.Amount) : double.NaN;   // blank, not 0.00, on the unused side
        set => SetAmount(-AppServices.ToCents(value), nameof(Outflow));
    }

    public double Inflow
    {
        get => Entity.Amount > 0 ? (double)AppServices.ToRands(Entity.Amount) : double.NaN;
        set => SetAmount(AppServices.ToCents(value), nameof(Inflow));
    }

    public string DateText => Entity.Date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    public string BankDateText => Entity.BankDate is { } bank ? bank.ToString("d MMM yyyy", CultureInfo.CurrentCulture) : "";
    public string ItemName => IsSplit ? $"Split ({LineCount} lines)" : _item.Id is null ? (NeedsItem ? "⚠ Choose a budget item" : "") : _item.Name;

    /// <summary>A budget account's transaction counts against an item, and so does a transfer between a budget account and a tracked one.</summary>
    private bool NeedsItem => AccountOnBudget && !(IsTransfer && OtherOnBudget);
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

    /// <param name="structural">An account changed, so the row has to be rebuilt from the saved data.</param>
    private void Changed(string[] names, bool dateChanged = false, bool splitChanged = false, bool structural = false)
    {
        foreach (var n in names) OnPropertyChanged(n);
        if (names.Contains(nameof(Payee))) OnPropertyChanged(nameof(PayeeText));
        if (IsEditing)
        {
            // Held until the row is committed (Enter, or choosing another row); Esc or a click away drops it.
            _dirty = true;
            _dirtyDate |= dateChanged;
            _dirtySplit |= splitChanged;
            _dirtyStructural |= structural;
            return;
        }
        _ = owner.RowChangedAsync(this, dateChanged, splitChanged, structural);
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

    /// <summary>For the first month: the date the budget starts on.</summary>
    public DateOnly? StartDate { get; init; }

    /// <summary>For the first month: the month it counts as (the first day of it), which need not be the month the start date is in.</summary>
    public DateOnly? StartMonth { get; init; }
    public bool CanDrag => !IsFirst;

    /// <summary>The solid line sits on the side of the month's own transactions: above the text when newest is first, below it when oldest is first.</summary>
    public bool LineBelow { get; init; }
    public bool LineAbove => !LineBelow;
    public string DateText => Entity is null ? "" : Entity.Date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    public string Label { get; } = label;

    public string Heading => $"{Label} starts here";

    public override string ToString() => Heading;
}

public sealed partial class TransactionsViewModel : ObservableObject
{
    // The filters stay as the user left them when moving between accounts.
    private static bool s_hideCleared;
    private static int s_sortIndex;

    private readonly BudgetStore _store = AppServices.Store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<AccountEntity> _accounts = new();
    private IReadOnlyList<PayeeHint> _hints = Array.Empty<PayeeHint>();
    private bool _loaded;
    private string _lastMonthLabel = "";

    /// <summary>The account this page shows, or null for all accounts.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Transaction rows and month lines, interleaved in time order.</summary>
    public ObservableCollection<object> Rows { get; } = new();
    public ObservableCollection<AccountOption> EntryAccounts { get; } = new();

    /// <summary>Payee names for the drop-down, kept in order as payees are added.</summary>
    public ObservableCollection<string> Payees { get; } = new();
    public ObservableCollection<ItemOption> ItemOptions { get; } = new();
    public ItemOption NoItem { get; } = new(null, "(no item)", null);

    [ObservableProperty] public partial string PageTitle { get; set; } = "";
    [ObservableProperty] public partial string AvailableText { get; set; } = "";
    [ObservableProperty] public partial string ClearedText { get; set; } = "";
    [ObservableProperty] public partial string UnclearedText { get; set; } = "";

    /// <summary>The selected list entry: a transaction or a month line.</summary>
    [ObservableProperty]
    public partial object? SelectedEntry { get; set; }

    public TransactionRow? Selected => SelectedEntry as TransactionRow;
    public MarkerRow? SelectedMarker => SelectedEntry as MarkerRow;
    public bool HasMarkerSelected => SelectedMarker is not null;
    public bool CanChangeSelectedMarker => SelectedMarker is { IsFirst: false };

    private TransactionRow? _editing;
    private TransactionRow? _draft;
    private bool _rebuilding;
    private DateOnly _budgetStart;
    private DateOnly _budgetStartMonth;

    /// <summary>
    /// Asked when something is dated before the budget starts: the date, and the date the budget starts on now. True means
    /// the user agreed to start the budget earlier.
    /// </summary>
    public Func<DateOnly, DateOnly, Task<bool>>? AskStartEarlierAsync { get; set; }

    /// <summary>
    /// True when the date is on or after the start of the budget. Otherwise the user is asked whether to start the budget
    /// from the date's month instead; if they agree it is moved (the months in between are added), and if not, false.
    /// </summary>
    public async Task<bool> EnsureWithinBudgetAsync(DateOnly date)
    {
        if (date >= _budgetStart) return true;
        var earlier = new DateOnly(date.Year, date.Month, 1);
        if (AskStartEarlierAsync is not { } ask || !await ask(date, _budgetStart))
        {
            Message = $"Not saved: {date:d MMM yyyy} is before the start of the budget ({_budgetStart:d MMM yyyy}).";
            return false;
        }

        await _gate.WaitAsync();
        try
        {
            var error = await _store.SetBudgetStartAsync(earlier);
            if (error is not null) { Message = error; return false; }
            _budgetStart = earlier;
            return true;
        }
        catch (Exception ex) { Message = $"Could not move the start of the budget: {ex.Message}"; return false; }
        finally { _gate.Release(); }
    }

    /// <summary>A new row that could not be saved when the user moved on stays selected, so what was typed is not lost.</summary>
    private async Task CommitOnLeaveAsync(TransactionRow row)
    {
        if (await row.CommitAsync() || !row.IsNew || !Rows.Contains(row)) return;
        await Task.Yield();
        SelectedEntry = row;
    }

    partial void OnSelectedEntryChanged(object? value)
    {
        // Choosing another row saves the edits made to the one being left.
        if (_editing is { IsDirty: true } leaving && !(_rebuilding && leaving.IsNew)) _ = CommitOnLeaveAsync(leaving);

        // The selected transaction is the one being edited in place.
        if (_editing is not null) _editing.IsEditing = false;
        _editing = value as TransactionRow;
        if (_editing is not null) _editing.IsEditing = true;
        foreach (var name in new[] { nameof(Selected), nameof(SelectedMarker), nameof(HasSelection), nameof(HasMarkerSelected), nameof(CanChangeSelectedMarker) })
            OnPropertyChanged(name);
    }

    /// <summary>0 = newest first, 1 = oldest first.</summary>
    [ObservableProperty]
    public partial int SortIndex { get; set; } = s_sortIndex;

    [ObservableProperty] public partial bool HideCleared { get; set; } = s_hideCleared;

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    public bool HasSelection => Selected is not null;
    public bool HasMessage => Message != "";

    /// <summary>A tracked account is outside the budget, so its page has no budget item column.</summary>
    /// <summary>Month lines belong to the budget, so a tracked account's page, which sits outside it, has no way to start a month.</summary>
    public bool CanStartMonth => ShowItemColumn;

    public bool ShowItemColumn => AccountId is null || (_accounts.FirstOrDefault(a => a.Id == AccountId)?.IsOnBudget ?? true);


    /// <summary>Each account's own page only lists that account, so the Account column is only for the all-accounts page.</summary>
    public bool ShowAccountColumn => AccountId is null;


    /// <summary>Tells the shared table layout which optional columns this page has.</summary>
    public void SyncColumnLayout()
    {
        // A transfer being added from a tracked account to a budget account still needs somewhere to choose its item.
        ColumnLayout.Current.ShowItem = ShowItemColumn || (_draft is { IsTransfer: true });
        ColumnLayout.Current.ShowAccount = ShowAccountColumn;
    }

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    partial void OnSortIndexChanged(int value)
    {
        s_sortIndex = value;
        if (_loaded) _ = ReloadAsync();
    }

    partial void OnHideClearedChanged(bool value)
    {
        s_hideCleared = value;
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
            await _store.EnsurePayeesFromTransactionsAsync();
            _hints = await _store.GetPayeeHintsAsync();
            Payees.Clear();
            foreach (var payee in await _store.GetPayeesAsync()) Payees.Add(payee.Name);

            PageTitle = AccountId is { } id ? _accounts.FirstOrDefault(a => a.Id == id)?.Name ?? "Account" : "All accounts";

            EntryAccounts.Clear();
            foreach (var a in _accounts.Where(a => !a.IsClosed || a.Id == AccountId))
                EntryAccounts.Add(new AccountOption(a.Id, a.IsClosed ? $"{a.Name} (closed)" : a.Name, a.IsOnBudget));

            ItemOptions.Clear();
            ItemOptions.Add(NoItem);
            foreach (var g in groups)
                foreach (var i in items.Where(i => i.GroupId == g.Id))
                    ItemOptions.Add(new ItemOption(i.Id, $"{g.Name} › {i.DisplayName}{(i.IsArchived ? " (archived)" : "")}", i.Kind, g.Id, i.Name));

            SyncColumnLayout();
            OnPropertyChanged(nameof(CanStartMonth));
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

    /// <summary>Recalculates the balances shown at the top. The caller must hold the gate.</summary>
    private async Task RefreshBalancesAsync(IReadOnlyList<MonthSummary>? months = null)
    {
        var balances = await _store.GetBalancesAsync(AccountId);
        AvailableText = Format.Money(balances.BookBalance);
        ClearedText = Format.Money(balances.ClearedBalance);
        UnclearedText = Format.Money(balances.UnclearedTotal);

        // What is left on each expense item this month, shown beside it in the item drop-down.
        months ??= BudgetEngine.Calculate(await _store.LoadSnapshotAsync());
        var remaining = months[^1].Items.ToDictionary(i => i.ItemId, i => i.Remaining);
        foreach (var option in ItemOptions)
            option.Balance = option.Id is { } id && remaining.TryGetValue(id, out var left) ? left : null;
    }

    /// <summary>Rebuilds the list. The caller must hold the gate.</summary>
    private async Task LoadRowsAsync(Guid? select)
    {
        var details = await _store.GetTransactionsAsync(AccountId);
        var snapshot = await _store.LoadSnapshotAsync();
        var months = BudgetEngine.Calculate(snapshot);
        _budgetStart = snapshot.BudgetStart ?? await _store.EnsureBudgetStartAsync();
        _budgetStartMonth = snapshot.BudgetStartMonth ?? new DateOnly(_budgetStart.Year, _budgetStart.Month, 1);
        _lastMonthLabel = months[^1].Label;
        await RefreshBalancesAsync(months);

        // Month lines are budget-wide, so they are hidden while looking at a tracked account.
        var viewedAccount = _accounts.FirstOrDefault(a => a.Id == AccountId);
        var showLines = viewedAccount is null || viewedAccount.IsOnBudget;
        SyncColumnLayout();

        var accountNames = _accounts.ToDictionary(a => a.Id, a => a.Name);
        var itemsById = ItemOptions.Where(o => o.Id is not null).ToDictionary(o => o.Id!.Value);
        var transferSides = (await _store.GetTransferLinksAsync()).ToLookup(t => t.PairId);

        // At equal keys a line sorts before a transaction, matching the engine: a transaction level with a line is after it.
        var entries = new List<(SortKey Key, int Rank, Guid Id, object Row)>();
        if (showLines)
        {
            var first = new MarkerRow(null, BudgetEngine.FirstMonthId, BudgetEngine.FirstMonthLabelFor(snapshot), this) { LineBelow = SortIndex == 1, StartDate = _budgetStart, StartMonth = _budgetStartMonth };
            entries.Add((new SortKey(DateOnly.MinValue, int.MinValue), 0, Guid.Empty, first));
            foreach (var m in await _store.GetMarkersAsync())
                entries.Add((new SortKey(m.Date, m.DayOrder), 0, m.Id, new MarkerRow(m, m.Id, m.Label, this) { LineBelow = SortIndex == 1 }));
        }
        foreach (var d in details)
        {
            if (HideCleared && d.Transaction.IsCleared) continue;
            var itemId = d.Lines.Count == 1 ? d.Lines[0].BudgetItemId : null;
            var option = itemId is { } id && itemsById.TryGetValue(id, out var found) ? found : NoItem;
            var otherSide = d.Transaction.TransferPairId is { } pair
                ? transferSides[pair].FirstOrDefault(t => t.TransactionId != d.Transaction.Id)
                : default;
            var row = new TransactionRow(d.Transaction, option, accountNames.GetValueOrDefault(d.Transaction.AccountId, "?"), d.Lines.Count, this)
            {
                OtherAccountId = otherSide.TransactionId == Guid.Empty ? null : otherSide.AccountId,
            };
            entries.Add((new SortKey(d.Transaction.Date, d.Transaction.DayOrder), 1, d.Transaction.Id, row));
        }

        var ordered = entries.OrderBy(e => e.Key).ThenBy(e => e.Rank).ThenBy(e => e.Id).Select(e => e.Row).ToList();
        if (SortIndex == 0) ordered.Reverse();

        var draft = _draft;
        _rebuilding = true;   // clearing the list must not save or drop a new row that is being filled in
        Rows.Clear();
        object? toSelect = null;
        foreach (var row in ordered)
        {
            Rows.Add(row);
            var id = row switch { TransactionRow t => t.Entity.Id, MarkerRow { Entity: { } m } => m.Id, _ => Guid.Empty };
            if (select is { } wanted && id == wanted) toSelect = row;
        }
        if (draft is not null && !draft.IsDirty) draft = null;
        if (draft is not null && toSelect is null)
        {
            if (SortIndex == 0) Rows.Insert(0, draft); else Rows.Add(draft);
            toSelect = draft;
        }
        else if (draft is not null) { draft.MarkDiscarded(); _draft = null; }
        SelectedEntry = toSelect;
        _rebuilding = false;
    }

    public async Task RowChangedAsync(TransactionRow row, bool dateChanged, bool splitChanged, bool structural)
    {
        await _gate.WaitAsync();
        try
        {
            if (dateChanged) row.Entity.DayOrder = await _store.NextDayOrderAsync(row.Entity.Date);
            string? note = null;

            if (row.IsTransfer)
            {
                // The edit is shared with the other side of the transfer, which also renames both sides.
                try { note = await _store.UpdateTransferAsync(row.Entity.Id, row.OtherAccountId, row.Item.Id); }
                catch (ArgumentException ex)
                {
                    Message = ex.Message;
                    await LoadRowsAsync(row.Entity.Id);   // shows the saved values again
                    return;
                }
            }
            else
            {
                var onBudget = _accounts.FirstOrDefault(a => a.Id == row.Entity.AccountId)?.IsOnBudget ?? true;
                if (!onBudget && row.IsSplit) await _store.ClearLineItemsAsync(row.Entity.Id);   // a tracked account has no budget items
                else if (splitChanged || (structural && !onBudget)) await _store.SetSingleSplitAsync(row.Entity.Id, onBudget ? row.Item.Id : null, row.Entity.Amount);
                else await _store.SaveAsync();
                if (structural && onBudget && !row.IsSplit && row.Item.Id is null) note = "Choose a budget item for this transaction.";
            }
            Message = note ?? "";

            // Clearing a row can make it drop out of a filtered list. A transfer's names and item depend on its accounts and amount.
            var disappears = HideCleared && row.IsCleared;
            if (dateChanged || structural || disappears || (row.IsTransfer && splitChanged))
            {
                // Keep whichever row is selected now: the edited one, or the one the user moved on to.
                var keep = row.IsEditing ? row.Entity.Id : SelectedId();
                await LoadRowsAsync(disappears ? null : keep);
            }
            else await RefreshBalancesAsync();
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// The user chose the date the budget starts on and the month it counts as. Earlier adds the months in between; later is
    /// refused while a transaction or month line sits before it.
    /// </summary>
    public async Task SetBudgetStartAsync(DateOnly date, DateOnly month)
    {
        await _gate.WaitAsync();
        try
        {
            Message = await _store.SetBudgetStartAsync(date, month) ?? "";
            await LoadRowsAsync(Guid.Empty);   // also puts the date shown back if it was refused
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>Changes the date a month line sits at, so the month starts on that day. Refused if it would pass another line.</summary>
    public async Task MoveSelectedMarkerToDateAsync(DateOnly date)
    {
        if (SelectedMarker is not { Entity: { } entity }) return;
        if (entity.Date == date) return;
        await _gate.WaitAsync();
        try
        {
            var order = await _store.NextDayOrderAsync(date);
            Message = await _store.MoveMarkerAsync(entity, new SortKey(date, order)) ? "" : "A month line can't be moved past another month line, or back before the start of the budget.";
            await LoadRowsAsync(entity.Id);   // also puts the date shown back if the move was refused
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>The label to suggest for a month starting on the given date: the month after the latest one.</summary>
    public string SuggestMonthLabel(DateTimeOffset date) => MonthLabels.Next(_lastMonthLabel, DateOnly.FromDateTime(date.DateTime));

    public async Task AddMonthAsync(string label, DateTimeOffset date)
    {
        await _gate.WaitAsync();
        try
        {
            var marker = await _store.AddMarkerAsync(label.Trim(), DateOnly.FromDateTime(date.DateTime), copyPreviousBudget: false);
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
                    Message = await _store.MoveMarkerAsync(entity, key) ? "" : "A month line can't be dragged past another month line, or back before the start of the budget.";
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

    private void AddPayeeName(string name)
    {
        if (name == "" || Payees.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase))) return;
        var index = 0;
        while (index < Payees.Count && string.Compare(Payees[index], name, StringComparison.OrdinalIgnoreCase) < 0) index++;
        Payees.Insert(index, name);
    }

    /// <summary>Creates a budget item in a category from the item drop-down, and adds it to the list.</summary>
    public async Task<ItemOption?> CreateItemAsync(Guid groupId, string name)
    {
        await _gate.WaitAsync();
        try
        {
            var group = (await _store.GetGroupsAsync()).Single(g => g.Id == groupId);
            var item = await _store.AddItemAsync(groupId, null, name.Trim(), group.Kind);
            var option = new ItemOption(item.Id, $"{group.Name} › {item.DisplayName}", item.Kind, group.Id, item.Name)
            {
                Balance = group.Kind == ItemKind.Expense ? 0 : null,
            };

            // Place it with the rest of its category.
            var lastInGroup = ItemOptions.Select((o, i) => (Option: o, Index: i)).LastOrDefault(x => x.Option.GroupId == groupId);
            ItemOptions.Insert(lastInGroup.Option is null ? ItemOptions.Count : lastInGroup.Index + 1, option);
            Message = "";
            return option;
        }
        catch (Exception ex) { Message = $"Could not add the budget item: {ex.Message}"; return null; }
        finally { _gate.Release(); }
    }

    /// <summary>Saves a payee typed into the drop-down, so it is in the list from now on.</summary>
    public async Task CreatePayeeAsync(string name)
    {
        await _gate.WaitAsync();
        try
        {
            var payee = await _store.EnsurePayeeAsync(name);
            AddPayeeName(payee.Name);
        }
        catch (Exception ex) { Message = $"Could not add the counterparty: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    // ---- New rows ----

    /// <summary>Asked before a new row dated on a month line is saved: which month it belongs to. Not OK means the user backed out.</summary>
    public Func<DateOnly, Task<(bool Ok, int? DayOrder)>>? AskMonthAsync { get; set; }

    public bool IsOnBudget(Guid accountId) => _accounts.FirstOrDefault(a => a.Id == accountId)?.IsOnBudget ?? true;

    private Guid? SelectedId() => SelectedEntry switch
    {
        TransactionRow { IsNew: false } t => t.Entity.Id,
        MarkerRow { Entity: { } m } => m.Id,
        _ => null,
    };

    /// <summary>
    /// Adds a new, editable row as the latest record, dated today, and selects it. The row is saved when the user presses
    /// Enter or chooses another row, and goes away on Esc. A transfer is a row whose Counterparty is the other account.
    /// </summary>
    public TransactionRow? BeginNew(bool transfer)
    {
        if (_draft is { } existing) { SelectedEntry = existing; return existing; }

        var open = EntryAccounts.Where(a => !a.Name.EndsWith("(closed)")).ToList();
        var account = (AccountId is { } fixedId ? EntryAccounts.FirstOrDefault(a => a.Id == fixedId) : null) ?? open.FirstOrDefault();
        if (account?.Id is not { } id) { Message = "Add an account first."; return null; }
        var other = transfer ? open.FirstOrDefault(a => a.Id != id) : null;
        if (transfer && other is null) { Message = "A transfer needs two accounts."; return null; }

        var entity = new TransactionEntity { AccountId = id, Date = DateOnly.FromDateTime(DateTime.Today), Payee = "" };
        if (transfer) entity.TransferPairId = Guid.CreateVersion7();   // marks it as a transfer until it is saved as a real pair
        var row = new TransactionRow(entity, NoItem, account.Name, 1, this) { IsNew = true, OtherAccountId = other?.Id };

        _draft = row;
        if (SortIndex == 0) Rows.Insert(0, row); else Rows.Add(row);   // the latest record: first when newest is first
        Message = "";
        SyncColumnLayout();
        SelectedEntry = row;
        return row;
    }

    /// <summary>The payee's usual item (and account, on the all-accounts page) fill in a new row.</summary>
    public void ApplyHint(TransactionRow row)
    {
        var hint = _hints.FirstOrDefault(h => string.Equals(h.Payee, row.Payee.Trim(), StringComparison.OrdinalIgnoreCase));
        if (hint is null) return;
        var item = hint.BudgetItemId is { } itemId ? ItemOptions.FirstOrDefault(o => o.Id == itemId) : null;
        row.ApplyHint(item, AccountId is null ? hint.AccountId : null);
    }

    /// <summary>Saves a new row. Returns false, with a message, when it is not complete enough to save yet.</summary>
    public async Task<bool> CommitNewAsync(TransactionRow row)
    {
        var e = row.Entity;
        var onBudget = IsOnBudget(e.AccountId);
        if (e.Amount == 0) { Message = "Enter either an outflow or an inflow."; return false; }
        if (row.IsTransfer)
        {
            if (row.OtherAccountId is null) { Message = "Choose the other account for this transfer."; return false; }
            if (row.OtherAccountId == e.AccountId) { Message = "Choose two different accounts."; return false; }
        }
        else if (row.DraftSplits is { } split && split.Sum(l => l.Amount) != e.Amount)
        {
            Message = "The split no longer adds up to the amount. Open Split again to fix it.";
            return false;
        }
        else if (onBudget && row.Item.Id is null && row.DraftSplits is null)
        {
            Message = "Choose a budget item for a budget account.";
            return false;
        }

        // A date that a month line sits on could be in either month, so ask which.
        int? dayOrder = null;
        var touchesBudget = onBudget || (row.IsTransfer && row.OtherAccountId is { } other && IsOnBudget(other));
        if (touchesBudget && !await EnsureWithinBudgetAsync(e.Date)) return false;
        if (touchesBudget && AskMonthAsync is { } ask)
        {
            var (ok, order) = await ask(e.Date);
            if (!ok) return false;
            dayOrder = order;
        }

        await _gate.WaitAsync();
        try
        {
            Guid newId;
            if (row.IsTransfer)
            {
                var (from, to) = e.Amount < 0 ? (e.AccountId, row.OtherAccountId!.Value) : (row.OtherAccountId!.Value, e.AccountId);
                var (outgoing, incoming) = await _store.AddTransferAsync(from, to, e.Date, Math.Abs(e.Amount),
                    string.IsNullOrWhiteSpace(e.Memo) ? null : e.Memo.Trim(), row.Item.Id, dayOrder);
                var mine = e.Amount < 0 ? outgoing : incoming;
                mine.IsCleared = e.IsCleared;
                mine.BankDate = e.BankDate;
                await _store.SaveAsync();
                newId = mine.Id;
            }
            else
            {
                e.Payee = e.Payee.Trim();
                e.Memo = string.IsNullOrWhiteSpace(e.Memo) ? null : e.Memo.Trim();
                e.DayOrder = dayOrder ?? await _store.NextDayOrderAsync(e.Date);
                SplitLineEntity[] lines = row.DraftSplits is { } draftLines
                    ? draftLines.Select(l => new SplitLineEntity { BudgetItemId = l.ItemId, Amount = l.Amount, Memo = l.Memo }).ToArray()
                    : [new SplitLineEntity { BudgetItemId = onBudget ? row.Item.Id : null, Amount = e.Amount }];
                await _store.AddTransactionAsync(e, lines);
                newId = e.Id;
                _hints = await _store.GetPayeeHintsAsync();
                AddPayeeName(e.Payee);
            }

            var stillSelected = SelectedEntry == row;
            row.MarkDiscarded();   // it is saved now; the list is rebuilt from the saved data
            _draft = null;
            Message = "";
            await LoadRowsAsync(stillSelected ? newId : SelectedId());
            return true;
        }
        catch (ArgumentException ex) { Message = ex.Message; return false; }
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

    /// <summary>Throws away the edits made to the row being edited, so it shows its old values, and lets go of it.</summary>
    public async Task CancelEditAsync()
    {
        if (_editing is { IsNew: true } draft)
        {
            draft.MarkDiscarded();   // a new row that is cancelled just goes away
            _draft = null;
            Rows.Remove(draft);
            SyncColumnLayout();
            Message = "";
        }
        else _editing?.Cancel();
        await DeselectAsync();
    }

    /// <summary>Saves the edits made to the row being edited and lets go of it.</summary>
    public async Task CommitEditAsync()
    {
        if (_editing is { } row && !await row.CommitAsync()) return;   // a new row that is not complete yet stays open
        await DeselectAsync();
    }

    /// <summary>
    /// Takes the selection off, so no row is being edited. It waits for any edit still being saved first, because
    /// saving some edits reloads the list and would select the row again.
    /// </summary>
    /// <summary>Remembers the column widths. It takes its turn with everything else the page does to the database.</summary>
    public async Task SaveColumnLayoutAsync(string layout)
    {
        await _gate.WaitAsync();
        try { await _store.SetLocalSettingAsync("txColumns", layout); }
        catch { /* not remembering the sizes must never get in the way */ }
        finally { _gate.Release(); }
    }

    public async Task DeselectAsync()
    {
        await _gate.WaitAsync();
        try { SelectedEntry = null; }
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

}
