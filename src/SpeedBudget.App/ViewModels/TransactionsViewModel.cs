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
public sealed partial class TransactionRow(TransactionEntity entity, ItemOption item, string accountName, int lineCount, bool accountOnBudget,
    bool showItemColumn, bool showAccountColumn, TransactionsViewModel owner) : ObservableObject
{
    /// <summary>Tracked accounts sit outside the budget, so their transactions have no budget item to show or edit.</summary>
    public bool AccountOnBudget { get; } = accountOnBudget;

    public bool ShowItemColumn { get; } = showItemColumn;
    public ColumnLayout Layout => ColumnLayout.Current;

    /// <summary>The lists the row's drop-downs choose from.</summary>
    public ObservableCollection<string> Payees => owner.Payees;
    public ObservableCollection<ItemOption> ItemOptions => owner.ItemOptions;
    public bool ShowAccountColumn { get; } = showAccountColumn;
    public TransactionEntity Entity { get; } = entity;
    public string AccountName { get; } = accountName;

    /// <summary>For a transfer: the account on its other side, and whether that account is part of the budget.</summary>
    public Guid? OtherAccountId { get; set; }
    public bool OtherOnBudget { get; init; }

    public ObservableCollection<AccountOption> Accounts => owner.EntryAccounts;

    /// <summary>The account this row's transaction is in, for the Account box.</summary>
    public AccountOption? AccountChoice
    {
        get => Accounts.FirstOrDefault(a => a.Id == Entity.AccountId);
        set
        {
            if (value?.Id is not { } id || id == Entity.AccountId) return;
            Entity.AccountId = id;
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
            Changed(new[] { nameof(OtherAccountChoice) }, structural: true);
        }
    }
    public int LineCount { get; } = lineCount;
    public bool IsTransfer => Entity.TransferPairId is not null;
    public bool IsSplit => LineCount > 1;

    /// <summary>The selected row shows editing boxes in place of its text.</summary>
    [ObservableProperty] public partial bool IsEditing { get; set; }

    // ---- Editing session: changes are held until committed, and can be thrown away ----

    private sealed record Snapshot(DateOnly Date, DateOnly? BankDate, string Payee, string? Memo, bool IsCleared, long Amount,
        Guid AccountId, ItemOption Item, Guid? OtherAccountId);

    private Snapshot? _before;
    private bool _dirtyDate, _dirtySplit, _dirtyStructural;

    /// <summary>The row has edits that have not been saved yet.</summary>
    public bool IsDirty { get; private set; }

    private void TakeSnapshot()
    {
        _before = new Snapshot(Entity.Date, Entity.BankDate, Entity.Payee, Entity.Memo, Entity.IsCleared, Entity.Amount,
            Entity.AccountId, _item, OtherAccountId);
        IsDirty = _dirtyDate = _dirtySplit = _dirtyStructural = false;
    }

    /// <summary>Saves the edits made since the row went into edit mode, if there are any.</summary>
    public async Task CommitAsync()
    {
        if (!IsDirty) return;
        var (date, split, structural) = (_dirtyDate, _dirtySplit, _dirtyStructural);
        TakeSnapshot();   // what is now saved is the new "before"
        await owner.RowChangedAsync(this, date, split, structural);
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
        IsDirty = _dirtyDate = _dirtySplit = _dirtyStructural = false;
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
    public bool ShowItemEditor => IsEditing && CanEditAmount && AccountOnBudget && !(IsTransfer && OtherOnBudget);
    public bool ShowSplitButton => IsEditing && IsSplit;
    public bool ShowItemText => !ShowItemEditor && !ShowSplitButton;

    /// <summary>A split's amount is fixed by its lines, which must add up. A transfer's amount is shared by its two sides.</summary>
    public bool CanEditAmount => !IsSplit;
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
            Changed(new[] { nameof(BankDateOffset), nameof(BankDateText), nameof(IsCleared) });
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
            IsDirty = true;
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

    partial void OnSelectedEntryChanged(object? value)
    {
        // Choosing another row saves the edits made to the one being left.
        if (_editing is { IsDirty: true } leaving) _ = leaving.CommitAsync();

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

    // Quick-add fields.
    [ObservableProperty] public partial DateTimeOffset NewDate { get; set; } = DateTimeOffset.Now;
    [ObservableProperty] public partial string NewPayee { get; set; } = "";
    [ObservableProperty] public partial string NewMemo { get; set; } = "";
    [ObservableProperty] public partial bool NewIsCleared { get; set; }
    [ObservableProperty] public partial double NewOutflow { get; set; } = double.NaN;
    [ObservableProperty] public partial double NewInflow { get; set; } = double.NaN;
    [ObservableProperty] public partial ItemOption? NewItem { get; set; }

    /// <summary>When the new transaction is split over several items: those lines (signed). Null for a single item.</summary>
    public IReadOnlyList<(Guid? ItemId, long Amount, string? Memo)>? NewSplits { get; private set; }

    /// <summary>Shown in the item box in place of an item while the new transaction is split.</summary>
    public string NewSplitText => NewSplits is { } lines ? $"Split ({lines.Count} lines)" : "";

    public void SetNewSplits(IReadOnlyList<(Guid? ItemId, long Amount, string? Memo)>? lines)
    {
        NewSplits = lines;
        OnPropertyChanged(nameof(NewSplitText));
    }

    partial void OnNewItemChanged(ItemOption? value)
    {
        if (NewSplits is not null) SetNewSplits(null);   // choosing a single item replaces the split
    }

    /// <summary>The total of the new transaction in cents, positive, and which way it goes (-1 out, 1 in); null until an amount is typed.</summary>
    public (long Total, int Sign)? NewAmount()
    {
        var outflow = AppServices.ToCents(NewOutflow);
        var inflow = AppServices.ToCents(NewInflow);
        if ((outflow == 0) == (inflow == 0) || outflow < 0 || inflow < 0) return null;
        return outflow != 0 ? (outflow, -1) : (inflow, 1);
    }
    [ObservableProperty] public partial AccountOption? NewAccount { get; set; }

    public bool HasSelection => Selected is not null;
    public bool HasMessage => Message != "";

    /// <summary>The account picker on the entry form is only needed when looking at all accounts.</summary>
    public bool ShowAccountPicker => AccountId is null;

    /// <summary>A tracked account is outside the budget, so its page has no budget item column.</summary>
    public bool ShowItemColumn => AccountId is null || (_accounts.FirstOrDefault(a => a.Id == AccountId)?.IsOnBudget ?? true);


    /// <summary>Each account's own page only lists that account, so the Account column is only for the all-accounts page.</summary>
    public bool ShowAccountColumn => AccountId is null;


    /// <summary>Tells the shared table layout which optional columns this page has.</summary>
    private void SyncColumnLayout()
    {
        ColumnLayout.Current.ShowItem = ShowItemColumn;
        ColumnLayout.Current.ShowAccount = ShowAccountColumn;
    }

    /// <summary>On the all-accounts page the item field follows the account chosen for the new transaction.</summary>
    public bool ShowItemPicker => AccountId is null ? NewAccount?.IsOnBudget ?? true : ShowItemColumn;

    public Microsoft.UI.Xaml.GridLength ItemFieldWidth => new(ShowItemPicker ? 210 : 0);

    partial void OnNewAccountChanged(AccountOption? value)
    {
        if (!ShowItemPicker) NewItem = NoItem;
        OnPropertyChanged(nameof(ShowItemPicker));
        OnPropertyChanged(nameof(ItemFieldWidth));
    }

    /// <summary>The how-to shown above the entry form. A tracked account is outside the budget, so it needs no budget item.</summary>
    public string CaptureGuidance => ShowItemColumn
        ? "Fill in the fields from left to right, then press Enter or Add. The counterparty is who the transaction is with: the person or business you paid, or the one who paid you. Pick or add one, choose the budget item it counts against, and type either an outflow (money out) or an inflow (money in). Tick Cleared if it already shows on your bank statement. Ctrl+N jumps here."
        : "Fill in the fields from left to right, then press Enter or Add. The counterparty is who the transaction is with: the person or business you paid, or the one who paid you. Pick or add one, and type either an outflow (money out) or an inflow (money in). This is a tracked account, outside your budget, so transactions here need no budget item. Tick Cleared if it already shows on your bank statement. Ctrl+N jumps here.";

    private void RaiseItemFieldChanged()
    {
        foreach (var name in new[] { nameof(ShowItemColumn), nameof(ShowAccountColumn), nameof(ShowItemPicker), nameof(ItemFieldWidth), nameof(CaptureGuidance) })
            OnPropertyChanged(name);
        SyncColumnLayout();
    }

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    /// <summary>Choosing a payee fills in the item and account it was last used with.</summary>
    partial void OnNewPayeeChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) ApplyHint(value);
    }

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

            NewAccount = AccountId is { } fixedId
                ? EntryAccounts.FirstOrDefault(a => a.Id == fixedId)
                : EntryAccounts.FirstOrDefault(a => !a.Name.EndsWith("(closed)"));
            NewItem = NoItem;
            RaiseItemFieldChanged();
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
        _lastMonthLabel = months[^1].Label;
        await RefreshBalancesAsync(months);

        // Month lines are budget-wide, so they are hidden while looking at a tracked account.
        var viewedAccount = _accounts.FirstOrDefault(a => a.Id == AccountId);
        var showLines = viewedAccount is null || viewedAccount.IsOnBudget;
        SyncColumnLayout();

        var accountNames = _accounts.ToDictionary(a => a.Id, a => a.Name);
        var accountsOnBudget = _accounts.ToDictionary(a => a.Id, a => a.IsOnBudget);
        var itemsById = ItemOptions.Where(o => o.Id is not null).ToDictionary(o => o.Id!.Value);
        var transferSides = (await _store.GetTransferLinksAsync()).ToLookup(t => t.PairId);

        // At equal keys a line sorts before a transaction, matching the engine: a transaction level with a line is after it.
        var entries = new List<(SortKey Key, int Rank, Guid Id, object Row)>();
        if (showLines)
        {
            var first = new MarkerRow(null, BudgetEngine.FirstMonthId, BudgetEngine.FirstMonthLabelFor(snapshot), this) { LineBelow = SortIndex == 1 };
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
            var row = new TransactionRow(d.Transaction, option, accountNames.GetValueOrDefault(d.Transaction.AccountId, "?"), d.Lines.Count,
                accountsOnBudget.GetValueOrDefault(d.Transaction.AccountId, true), ShowItemColumn, ShowAccountColumn, this)
            {
                OtherAccountId = otherSide.TransactionId == Guid.Empty ? null : otherSide.AccountId,
                OtherOnBudget = otherSide.TransactionId != Guid.Empty && accountsOnBudget.GetValueOrDefault(otherSide.AccountId, true),
            };
            entries.Add((new SortKey(d.Transaction.Date, d.Transaction.DayOrder), 1, d.Transaction.Id, row));
        }

        var ordered = entries.OrderBy(e => e.Key).ThenBy(e => e.Rank).ThenBy(e => e.Id).Select(e => e.Row).ToList();
        if (SortIndex == 0) ordered.Reverse();

        Rows.Clear();
        object? toSelect = null;
        foreach (var row in ordered)
        {
            Rows.Add(row);
            var id = row switch { TransactionRow t => t.Entity.Id, MarkerRow { Entity: { } m } => m.Id, _ => Guid.Empty };
            if (select is { } wanted && id == wanted) toSelect = row;
        }
        SelectedEntry = toSelect;
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
                var keep = row.IsEditing ? row.Entity.Id : SelectedEntry switch
                {
                    TransactionRow t => t.Entity.Id,
                    MarkerRow { Entity: { } m } => m.Id,
                    _ => (Guid?)null,
                };
                await LoadRowsAsync(disappears ? null : keep);
            }
            else await RefreshBalancesAsync();
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// The user chose which month the first month is. The first month is normally worked out from the data, but deleting
    /// transactions can change that, so this lets the user say which month it really is.
    /// </summary>
    public async Task SetFirstMonthAsync(int year, int month)
    {
        await _gate.WaitAsync();
        try
        {
            await _store.RenameFirstMonthAsync(MonthLabels.For(new DateOnly(year, month, 1)));
            Message = "";
            await LoadRowsAsync(Guid.Empty);
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
            Message = await _store.MoveMarkerAsync(entity, new SortKey(date, order)) ? "" : "A month line can't be moved past another month line.";
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

    /// <summary>Fills in the item and account last used with this payee, if those are still unset.</summary>
    public void ApplyHint(string payee)
    {
        var hint = _hints.FirstOrDefault(h => string.Equals(h.Payee, payee.Trim(), StringComparison.OrdinalIgnoreCase));
        if (hint is null) return;
        if (NewItem?.Id is null && hint.BudgetItemId is { } itemId)
            NewItem = ItemOptions.FirstOrDefault(o => o.Id == itemId) ?? NoItem;
        if (AccountId is null)
            NewAccount = EntryAccounts.FirstOrDefault(a => a.Id == hint.AccountId) ?? NewAccount;
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

    /// <summary>Returns true when the transaction was added.</summary>
    /// <param name="dayOrder">Where in its day the transaction sits. Only given when the date is a month line's date, to say which month it is in.</param>
    public async Task<bool> AddAsync(int? dayOrder = null)
    {
        var accountOption = NewAccount;
        if (accountOption?.Id is not { } accountId) { Message = "Choose an account."; return false; }

        var outflow = AppServices.ToCents(NewOutflow);
        var inflow = AppServices.ToCents(NewInflow);
        if ((outflow == 0) == (inflow == 0)) { Message = "Enter either an outflow or an inflow."; return false; }
        if (outflow < 0 || inflow < 0) { Message = "Amounts must be positive. Use outflow or inflow to say which way the money went."; return false; }
        if (NewSplits is { } split && split.Sum(l => l.Amount) != inflow - outflow)
        {
            Message = "The split no longer adds up to the amount. Open Split again to fix it.";
            return false;
        }
        if (accountOption.IsOnBudget && NewItem?.Id is null && NewSplits is null) { Message = "Choose a budget item for a budget account."; return false; }

        await _gate.WaitAsync();
        try
        {
            var date = DateOnly.FromDateTime(NewDate.DateTime);
            var amount = inflow - outflow;
            var tx = await _store.AddTransactionAsync(
                new TransactionEntity
                {
                    AccountId = accountId, Date = date, DayOrder = dayOrder ?? await _store.NextDayOrderAsync(date),
                    Payee = NewPayee.Trim(), Memo = string.IsNullOrWhiteSpace(NewMemo) ? null : NewMemo.Trim(), Amount = amount,
                    IsCleared = NewIsCleared,
                },
                NewSplits is { } lines
                    ? lines.Select(l => new SplitLineEntity { BudgetItemId = l.ItemId, Amount = l.Amount, Memo = l.Memo }).ToArray()
                    : new[] { new SplitLineEntity { BudgetItemId = NewItem?.Id, Amount = amount } });

            _hints = await _store.GetPayeeHintsAsync();
            AddPayeeName(NewPayee.Trim());
            NewPayee = "";
            NewMemo = "";
            SetNewSplits(null);
            NewIsCleared = false;
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

    /// <summary>Throws away the edits made to the row being edited, so it shows its old values, and lets go of it.</summary>
    public async Task CancelEditAsync()
    {
        _editing?.Cancel();
        await DeselectAsync();
    }

    /// <summary>Saves the edits made to the row being edited and lets go of it.</summary>
    public async Task CommitEditAsync()
    {
        if (_editing is { } row) await row.CommitAsync();
        await DeselectAsync();
    }

    /// <summary>
    /// Takes the selection off, so no row is being edited. It waits for any edit still being saved first, because
    /// saving some edits reloads the list and would select the row again.
    /// </summary>
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

    /// <summary>Returns an error sentence, or null when the transfer was saved.</summary>
    public async Task<string?> AddTransferAsync(AccountOption? from, AccountOption? to, DateTimeOffset date, double amount, string memo, ItemOption? item, int? dayOrder = null)
    {
        if (from?.Id is not { } fromId || to?.Id is not { } toId) return "Choose both accounts.";
        await _gate.WaitAsync();
        try
        {
            await _store.AddTransferAsync(fromId, toId, DateOnly.FromDateTime(date.DateTime), AppServices.ToCents(amount),
                string.IsNullOrWhiteSpace(memo) ? null : memo.Trim(), item?.Id, dayOrder);
            await LoadRowsAsync(null);
            return null;
        }
        catch (ArgumentException ex) { return ex.Message; }
        catch (Exception ex) { return $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }
}
