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
public sealed class ItemOption(Guid? id, string name, ItemKind? kind, Guid? groupId = null, string shortName = "", string label = "")
{
    public Guid? Id { get; } = id;
    public string Name { get; } = name;
    public ItemKind? Kind { get; } = kind;
    public Guid? GroupId { get; } = groupId;

    /// <summary>The name of the group the item is in (an item's full name reads "Group › Item").</summary>
    public string GroupName => Name.Contains(" › ") ? Name[..Name.IndexOf(" › ", StringComparison.Ordinal)] : "";

    /// <summary>The item on its own, as shown in a table row or a closed drop-down: its icon and name, without its group.</summary>
    public string Label { get; } = label == "" ? name : label;

    /// <summary>The item's own name, without its group or emoji, for telling whether typed text already exists.</summary>
    public string ShortName { get; } = shortName;

    /// <summary>True for the "add a new item" row at the top of a search, which is not a real item.</summary>
    public bool IsCreateRow { get; init; }

    /// <summary>True for the "split between several items" row at the top of the list, which is not a real item.</summary>
    public bool IsSplitRow { get; init; }

    /// <summary>Whether the pencil for editing this item shows. Set by the drop-down that lists it.</summary>
    public bool PencilShown { get; set; }
    public bool CanEdit => PencilShown;

    /// <summary>True for the few items used most, which the drop-down lists first.</summary>
    public bool IsFrequent { get; set; }
    public int UseCount { get; set; }
    public Microsoft.UI.Xaml.Visibility FrequentShown => IsFrequent ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public long? Balance { get; set; }
    public string BalanceText => Balance is { } b ? Format.Money(b) : "";
    public Microsoft.UI.Xaml.Media.Brush BalanceBrush => Balance is { } b ? Format.Signed(b) : Format.Theme("TextFillColorPrimaryBrush");
}

/// <summary>Asks the transactions page to list only what makes up one figure on the budget: an item in a month.</summary>
public sealed record TransactionDrillDown(Guid MonthId, Guid ItemId);

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

    /// <summary>Whether the row has a tick box: a new row that is not saved yet has none.</summary>
    public bool ShowTick => !IsNew;

    /// <summary>Ticked rows are deleted together (see the Delete selected button and the right-click menu).</summary>
    [ObservableProperty] public partial bool IsTicked { get; set; }
    partial void OnIsTickedChanged(bool value) => owner.TicksChanged();

    public ColumnLayout Layout => ColumnLayout.Current;

    /// <summary>The lists the row's drop-downs choose from.</summary>
    public ObservableCollection<string> Payees => owner.Payees;
    public IReadOnlyDictionary<string, string> PayeeIcons => owner.PayeeIcons;
    public ObservableCollection<ItemOption> ItemOptions => owner.ItemOptions;

    /// <summary>A counterparty's name or icon was edited: show the row's counterparty as it is now.</summary>
    public void RefreshPayee()
    {
        OnPropertyChanged(nameof(Payee));
        OnPropertyChanged(nameof(PayeeText));
    }

    /// <summary>The budget items were edited: point the row at the current version of its item.</summary>
    public void RefreshItem()
    {
        if (_item.Id is { } id) _item = owner.ItemOptions.FirstOrDefault(o => o.Id == id) ?? owner.NoItem;
        OnPropertyChanged(nameof(Item));
        OnPropertyChanged(nameof(ItemName));
    }
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

    /// <summary>True when the row's amount was worked out from its split lines (not typed), so the split dialog adds the lines up instead of asking for them to match.</summary>
    public bool AmountFromSplit { get; private set; }

    /// <summary>Sets the split lines of a new row and makes the row's amount their total (money out or in, as the lines are signed).</summary>
    public void SetSplitFromLines(IReadOnlyList<(Guid? ItemId, long Amount, string? Memo)> lines)
    {
        Entity.Amount = lines.Sum(l => l.Amount);
        AmountFromSplit = true;
        SetDraftSplits(lines);
        foreach (var n in new[] { nameof(Outflow), nameof(Inflow), nameof(OutflowText), nameof(InflowText) }) OnPropertyChanged(n);
    }

    /// <summary>Sets the split lines of a new row. A single line is just an item.</summary>
    public void SetDraftSplits(IReadOnlyList<(Guid? ItemId, long Amount, string? Memo)> lines)
    {
        _draftSplits = lines.Count > 1 ? lines : null;
        if (lines.Count == 1) _item = owner.ItemOptions.FirstOrDefault(o => o.Id == lines[0].ItemId) ?? owner.NoItem;
        foreach (var n in new[] { nameof(LineCount), nameof(IsSplit), nameof(ItemName), nameof(Item), nameof(CanEditAmount), nameof(ShowAmountEditor),
                                  nameof(ShowAmountText), nameof(ShowItemEditor), nameof(SplitLabel), nameof(ShowItemText) })
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
    public int LineCount => _draftSplits?.Count ?? _lineOverride ?? lineCount;

    // Set when a split transaction is given a single budget item: it has one line from then on, before the save has happened.
    private int? _lineOverride;
    public bool IsTransfer => Entity.TransferPairId is not null;
    public bool IsSplit => LineCount > 1;

    /// <summary>The selected row shows editing boxes in place of its text.</summary>
    [ObservableProperty] public partial bool IsEditing { get; set; }

    // ---- Editing session: changes are held until committed, and can be thrown away ----

    private sealed record Snapshot(DateOnly Date, bool Undated, DateOnly? BankDate, string Payee, string? Memo, bool IsCleared, long Amount,
        Guid AccountId, ItemOption Item, Guid? OtherAccountId, int? LineOverride, IReadOnlyList<(Guid? ItemId, long Amount, string? Memo)>? Splits);

    private Snapshot? _before;
    private bool _dirtyDate, _dirtySplit, _dirtyStructural;

    private bool _dirty;

    /// <summary>The row has edits that have not been saved yet. A new row always has.</summary>
    public bool IsDirty => !_discarded && (IsNew || _dirty);

    private void TakeSnapshot()
    {
        _before = new Snapshot(Entity.Date, Entity.IsUndated, Entity.BankDate, Entity.Payee, Entity.Memo, Entity.IsCleared, Entity.Amount,
            Entity.AccountId, _item, OtherAccountId, _lineOverride, _draftSplits);
        _dirty = _dirtyDate = _dirtySplit = _dirtyStructural = false;
    }

    /// <summary>Saves the edits made since the row went into edit mode, if there are any. False when a new row could not be saved yet.</summary>
    public async Task<bool> CommitAsync()
    {
        if (IsNew) return _discarded || await owner.CommitNewAsync(this);
        if (!_dirty) return true;
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
        Entity.IsUndated = b.Undated;
        Entity.BankDate = b.BankDate;
        Entity.Payee = b.Payee;
        Entity.Memo = b.Memo;
        Entity.IsCleared = b.IsCleared;
        Entity.Amount = b.Amount;
        Entity.AccountId = b.AccountId;
        _item = b.Item;
        OtherAccountId = b.OtherAccountId;
        _lineOverride = b.LineOverride;
        _draftSplits = b.Splits;
        _dirty = _dirtyDate = _dirtySplit = _dirtyStructural = false;
        OnPropertyChanged(string.Empty);   // every box shows its old value again
    }

    partial void OnIsEditingChanged(bool value)
    {
        if (value) TakeSnapshot();
        foreach (var n in new[] { nameof(ShowPayeeEditor), nameof(ShowTransferEditor), nameof(ShowPayeeText), nameof(ShowAmountEditor), nameof(ShowAmountText),
                                  nameof(ShowItemEditor), nameof(SplitLabel), nameof(ShowItemText), nameof(NotEditing), nameof(ExpectedOpacity), nameof(ShowClearDate) })
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
    public bool ShowItemEditor => IsEditing
        && (IsTransfer ? (IsNew ? AccountOnBudget != OtherOnBudget : AccountOnBudget && !OtherOnBudget) : AccountOnBudget);
    public bool ShowItemText => !ShowItemEditor;

    /// <summary>What the budget item box says for a split transaction, in place of an item; empty for any other.</summary>
    public string SplitLabel => IsSplit ? $"Split ({LineCount} lines)" : "";

    /// <summary>A split's amount is fixed by its lines, which must add up. A transfer's amount is shared by its two sides.</summary>
    public bool CanEditAmount => !IsSplit;
    public bool CanSplit => !IsTransfer;

    /// <summary>The row was dropped, or saved under another row, so it must not be saved again.</summary>
    private bool _discarded;
    public void MarkDiscarded() => _discarded = true;

    private ItemOption _item = item;

    private static readonly string[] DateNames =
        [nameof(DateOffset), nameof(DateText), nameof(IsExpectedNow), nameof(ExpectedOpacity), nameof(ExpectedFontStyle), nameof(HasDate), nameof(ShowClearDate)];

    /// <summary>
    /// The date the transaction happened. Clearing it, when there is no bank date either, makes the transaction expected with no date: it is
    /// shown greyed out and in italics and changes no balance, until it is given a date (or a bank date).
    /// </summary>
    public DateTimeOffset? DateOffset
    {
        get => Entity.IsUndated ? null : new DateTimeOffset(Entity.Date.ToDateTime(TimeOnly.MinValue));
        set
        {
            if (value is not { } v)
            {
                if (Entity.IsUndated) return;
                if (Entity.BankDate is { } bank)
                {
                    if (bank == Entity.Date) return;
                    Entity.Date = bank;   // the bank date stands in for it
                }
                else
                {
                    Entity.IsUndated = true;
                    Entity.Date = TransactionEntity.UndatedPlace;
                }
                Changed(DateNames, dateChanged: true);
                return;
            }
            var date = DateOnly.FromDateTime(v.DateTime);
            if (date == Entity.Date && !Entity.IsUndated) return;
            Entity.IsUndated = false;
            Entity.Date = date;
            Changed(DateNames, dateChanged: true);
        }
    }

    /// <summary>False for an expected transaction that has no date yet.</summary>
    public bool HasDate => !Entity.IsUndated;
    public bool ShowClearDate => IsEditing && HasDate;

    /// <summary>Scheduled (dated in the future) or without a date: it has not happened yet, so it is greyed out and in italics, and changes no balance.</summary>
    public bool IsExpectedNow => Entity.IsExpected(DateOnly.FromDateTime(DateTime.Today));
    public double ExpectedOpacity => IsExpectedNow && !IsEditing ? 0.5 : 1;
    public Windows.UI.Text.FontStyle ExpectedFontStyle => IsExpectedNow ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;

    public DateTimeOffset? BankDateOffset
    {
        get => Entity.BankDate is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue)) : null;
        set
        {
            DateOnly? date = value is { } v ? DateOnly.FromDateTime(v.DateTime) : null;
            if (date == Entity.BankDate) return;
            var wasUndated = Entity.IsUndated;
            Entity.BankDate = date;
            // As in the sheet, entering a bank date clears the transaction. An expected transaction with no date becomes an ordinary one, on that date.
            if (date is not null)
            {
                Entity.IsCleared = true;
                if (wasUndated) { Entity.IsUndated = false; Entity.Date = date.Value; }
            }
            var names = new List<string> { nameof(BankDateOffset), nameof(BankDateText), nameof(IsCleared) };
            if (wasUndated && date is not null) names.AddRange(DateNames);
            Changed(names.ToArray(), dateChanged: wasUndated && date is not null);
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
        set { if ((Entity.Memo ?? "") == value) return; Entity.Memo = value; Changed(nameof(Memo)); OnPropertyChanged(nameof(MemoTip)); OnPropertyChanged(nameof(MemoIconOpacity)); }
    }

    /// <summary>For the narrow table, where the memo is an icon: its text is the tooltip, and the icon is faint when there is no memo.</summary>
    public string MemoTip => string.IsNullOrWhiteSpace(Memo) ? "Add a memo" : Memo;
    public double MemoIconOpacity => string.IsNullOrWhiteSpace(Memo) ? 0.35 : 1;

    /// <summary>Agreed with the bank's file: it shows a green R instead of the cleared tick.</summary>
    public bool IsReconciled => Entity.IsReconciled;
    public bool NotReconciled => !Entity.IsReconciled;

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
            var wasSplit = IsSplit;
            _item = value;
            if (wasSplit)
            {
                // Choosing one item for a split transaction turns it back into an ordinary one with that item.
                _draftSplits = null;
                _lineOverride = 1;
                AmountFromSplit = false;
                foreach (var n in new[] { nameof(LineCount), nameof(IsSplit), nameof(SplitLabel), nameof(CanEditAmount), nameof(ShowAmountEditor), nameof(ShowAmountText) })
                    OnPropertyChanged(n);
            }
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

    public string DateText => Entity.IsUndated ? "" : Entity.Date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    public string BankDateText => Entity.BankDate is { } bank ? bank.ToString("d MMM yyyy", CultureInfo.CurrentCulture) : "";
    /// <summary>A transfer between two accounts that are both in the budget (or both outside it) has no budget item: it moves money, not budget.</summary>
    public string ItemName => IsSplit ? $"Split ({LineCount} lines)"
        : IsTransfer && _item.Id is null && AccountOnBudget == OtherOnBudget ? "Not Applicable"
        : _item.Id is null ? "" : _item.Label;

    /// <summary>What the column shows for this row, which is what the column's filter lists and matches. An empty cell is an empty string.</summary>
    public string FilterValue(FilterColumn column) => column switch
    {
        FilterColumn.Date => DateText,
        FilterColumn.BankDate => BankDateText,
        FilterColumn.Counterparty => Entity.Payee ?? "",
        FilterColumn.Memo => Entity.Memo ?? "",
        FilterColumn.Item => ItemName,
        FilterColumn.Account => AccountName,
        FilterColumn.Outflow => OutflowText,
        FilterColumn.Inflow => InflowText,
        FilterColumn.Cleared => Entity.IsCleared ? "Cleared" : "Uncleared",
        _ => "",
    };

    /// <summary>What the column's values are ordered by in its filter list: dates by date and amounts by size, the rest alphabetically.</summary>
    public IComparable FilterOrder(FilterColumn column) => column switch
    {
        FilterColumn.Date => Entity.Date,
        FilterColumn.BankDate => Entity.BankDate ?? DateOnly.MinValue,
        FilterColumn.Outflow or FilterColumn.Inflow => Math.Abs(Entity.Amount),
        _ => FilterValue(column).ToLowerInvariant(),
    };

    public string OutflowText => Entity.Amount < 0 ? AppServices.ToRands(-Entity.Amount).ToString("N2") : "";
    public string InflowText => Entity.Amount > 0 ? AppServices.ToRands(Entity.Amount).ToString("N2") : "";
    public string PayeeText => IsTransfer ? $"⇄ {Entity.Payee}" : owner.WithIcon(Entity.Payee);

    public override string ToString() => $"{DateText} {Entity.Payee}";

    private void SetAmount(long cents, string source)
    {
        // Entering 0 in the box that is not currently in use must not wipe the other side.
        if (cents == 0 && ((source == nameof(Outflow) && Entity.Amount > 0) || (source == nameof(Inflow) && Entity.Amount < 0))) return;
        if (cents == Entity.Amount) return;
        Entity.Amount = cents;
        AmountFromSplit = false;
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

    public bool CanDrag => !IsFirst;

    /// <summary>The solid line sits on the side of the month's own transactions: above the text when newest is first, below it when oldest is first.</summary>
    public bool LineBelow { get; init; }
    public bool LineAbove => !LineBelow;

    public string Label { get; } = label;

    public string Heading => $"{Label} starts here";

    /// <summary>What the side panel says while it is only showing the line, not editing it.</summary>
    public string ViewText => IsFirst
        ? $"Your budget starts in {Label}. Everything after this line, down to the next month line, counts towards it."
        : $"Everything after this line, down to the next month line, counts towards {Label}.";

    /// <summary>The month this line stands for, as the first day of it.</summary>
    public DateOnly? Month => MonthLabels.TryParse(Label, out var month) ? month : null;

    public override string ToString() => Heading;
}

public sealed partial class TransactionsViewModel : ObservableObject
{
    // The filters stay as the user left them when moving between accounts.
    private static bool s_hideCleared;
    private static bool s_hideReconciled;
    private static int s_sortIndex = 1;   // oldest first, unless the user chooses otherwise

    private readonly BudgetStore _store = AppServices.Store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TransactionsViewModel()
    {
        Filters.Changed += OnFiltersChanged;
        Rows.CollectionChanged += (_, _) => TicksChanged();
    }


    private List<AccountEntity> _accounts = new();
    /// <summary>The next load builds every row afresh: the entities were changed in place (a bank import), so a row cannot tell by its figures that it is out of date.</summary>
    private bool _rebuildAll;
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

    /// <summary>The icon of each counterparty that has one, by name.</summary>
    public Dictionary<string, string> PayeeIcons { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A counterparty's name with its icon in front, when it has one.</summary>
    public string WithIcon(string name) => PayeeIcons.TryGetValue(name, out var icon) && icon != "" ? $"{icon} {name}" : name;

    public ObservableCollection<ItemOption> ItemOptions { get; } = new();
    public ItemOption NoItem { get; } = new(null, "(no item)", null);

    [ObservableProperty] public partial string PageTitle { get; set; } = "";

    /// <summary>The label in the top right corner saying whether this account is in the budget or only tracked.</summary>
    [ObservableProperty] public partial bool ShowAccountBadge { get; set; }
    [ObservableProperty] public partial string AccountBadgeText { get; set; } = "";
    [ObservableProperty] public partial Microsoft.UI.Xaml.Controls.IconElement? AccountBadgeIcon { get; set; }
    [ObservableProperty] public partial string AccountBadgeTip { get; set; } = "";
    [ObservableProperty] public partial Microsoft.UI.Xaml.Media.Brush? AccountBadgeBrush { get; set; }
    [ObservableProperty] public partial Microsoft.UI.Xaml.Media.Brush? AccountBadgeTextBrush { get; set; }
    [ObservableProperty] public partial Microsoft.UI.Xaml.Media.Brush? AccountBadgeBorderBrush { get; set; }
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

    /// <summary>The side panel for a month line only shows it until the pencil is pressed; then its boxes open until Save or Cancel.</summary>
    [ObservableProperty] public partial bool IsMarkerEditing { get; set; }
    public bool ShowMarkerView => !IsMarkerEditing;
    public bool ShowMarkerEditButton => HasMarkerSelected && !IsMarkerEditing;
    partial void OnIsMarkerEditingChanged(bool value) => RaiseMarkerPanel();
    private void RaiseMarkerPanel()
    {
        foreach (var name in new[] { nameof(ShowMarkerView), nameof(ShowMarkerEditButton) })
            OnPropertyChanged(name);
    }

    // ---- Editing: any number of rows can be open for editing at the same time ----

    private readonly List<TransactionRow> _editingRows = new();

    /// <summary>The rows that are open for editing now.</summary>
    public IReadOnlyList<TransactionRow> EditingRows => _editingRows;

    /// <summary>True while at least one row is open for editing. A row stays open until it is saved (Enter, or its green check) or dropped (Esc).</summary>
    public bool IsRowEditing => _editingRows.Any(r => r.IsEditing);

    /// <summary>Turns edit mode on for a row (the pencil, or a new row). Rows that are already open stay open.</summary>
    public void BeginEdit(TransactionRow row)
    {
        if (!ReferenceEquals(SelectedEntry, row)) SelectedEntry = row;
        if (!_editingRows.Contains(row)) _editingRows.Add(row);
        row.IsEditing = true;
        OnPropertyChanged(nameof(IsRowEditing));
    }

    /// <summary>Opens several rows for editing together (the rows ticked in multi-select mode).</summary>
    public void BeginEditMany(IEnumerable<TransactionRow> rows)
    {
        foreach (var row in rows.Where(r => !r.IsNew).ToList()) BeginEdit(row);
    }

    private void CloseEditing(TransactionRow row)
    {
        row.IsEditing = false;
        _editingRows.Remove(row);
        OnPropertyChanged(nameof(IsRowEditing));
    }

    /// <summary>Saves one row and takes it out of edit mode. A new row that is not complete yet stays open.</summary>
    public async Task CommitRowAsync(TransactionRow row)
    {
        if (!await row.CommitAsync()) return;
        CloseEditing(row);
        await DeselectIfIdleAsync();
    }

    /// <summary>Saves every row that is open, one after the other. A row that cannot be saved yet stays open.</summary>
    public async Task CommitAllEditsAsync()
    {
        foreach (var row in _editingRows.ToList()) await CommitRowAsync(row);
    }

    /// <summary>Throws away the edits made to one row, so it shows its old values, and takes it out of edit mode. A new row just goes away.</summary>
    public async Task CancelRowAsync(TransactionRow row)
    {
        if (row.IsNew)
        {
            row.MarkDiscarded();
            if (ReferenceEquals(_draft, row)) _draft = null;
            Rows.Remove(row);
            SyncColumnLayout();
            Message = "";
        }
        else row.Cancel();
        CloseEditing(row);
        await DeselectIfIdleAsync();
    }

    /// <summary>Throws away the edits to every row that is open (and clears the highlight).</summary>
    public async Task CancelEditAsync()
    {
        foreach (var row in _editingRows.ToList()) await CancelRowAsync(row);
        await DeselectAsync();
    }

    /// <summary>Once the highlighted row is not being edited, the highlight goes.</summary>
    private async Task DeselectIfIdleAsync()
    {
        if (SelectedEntry is TransactionRow { IsEditing: true }) return;
        await DeselectAsync();
    }

    private TransactionRow? _draft;
    private bool _rebuilding;

    partial void OnSelectedEntryChanged(object? value)
    {
        // Selecting a row only highlights it. Rows are edited in place only after their pencil is pressed, and stay open until saved or dropped.
        IsMarkerEditing = false;   // a month line is always looked at first
        foreach (var name in new[] { nameof(Selected), nameof(SelectedMarker), nameof(HasSelection), nameof(HasMarkerSelected), nameof(CanChangeSelectedMarker) })
            OnPropertyChanged(name);
        RaiseMarkerPanel();
    }

    /// <summary>0 = newest first, 1 = oldest first.</summary>
    [ObservableProperty]
    public partial int SortIndex { get; set; } = s_sortIndex;

    [ObservableProperty] public partial bool HideCleared { get; set; } = s_hideCleared;

    /// <summary>Hide the transactions that agree with the bank's file (they are cleared too, so this matters when cleared ones are shown).</summary>
    [ObservableProperty] public partial bool HideReconciled { get; set; } = s_hideReconciled;

    /// <summary>How far back the table goes: 0 = everything, 1 = the last week, 2 = the last month, 3 = the last quarter (as on the balance tables).</summary>
    [ObservableProperty] public partial int RangeIndex { get; set; } = s_range;
    private static int s_range;

    /// <summary>The first date shown, or null when everything is.</summary>
    private DateOnly? RangeStart()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return RangeIndex switch { 1 => today.AddDays(-7), 2 => today.AddMonths(-1), 3 => today.AddMonths(-3), _ => null };
    }

    private bool Hidden(TransactionEntity t) =>
        (HideCleared && t.IsCleared) || (HideReconciled && t.IsReconciled) || (RangeStart() is { } start && t.Date < start);

    /// <summary>The eye's tip: what is hidden now.</summary>
    public string HideTip
    {
        get
        {
            var parts = new List<string>();
            if (HideCleared) parts.Add("hiding cleared transactions");
            if (HideReconciled) parts.Add("hiding reconciled transactions");
            if (RangeIndex != 0) parts.Add(RangeIndex switch { 1 => "showing only the last week", 2 => "showing only the last month", _ => "showing only the last quarter" });
            if (parts.Count == 0) return "Showing every transaction. Click to choose what to hide";
            var text = string.Join(" and ", parts);
            return char.ToUpper(text[0]) + text[1..];
        }
    }

    partial void OnRangeIndexChanged(int value)
    {
        s_range = value;
        OnPropertyChanged(nameof(HideTip));
        OnPropertyChanged(nameof(HideBrush));
        if (_loaded) _ = ReloadAsync();
    }

    /// <summary>What the eye menu offers: to show what is hidden, or to hide what is showing.</summary>
    public string ClearedItemText => HideCleared ? "Show cleared transactions" : "Hide cleared transactions";
    public string ReconciledItemText => HideReconciled ? "Show reconciled transactions" : "Hide reconciled transactions";

    /// <summary>The eye is coloured while anything is hidden.</summary>
    public Microsoft.UI.Xaml.Media.Brush HideBrush => Format.Theme(HideCleared || HideReconciled || RangeIndex != 0 ? "AccentTextFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");

    partial void OnHideReconciledChanged(bool value)
    {
        s_hideReconciled = value;
        OnPropertyChanged(nameof(ReconciledItemText));
        OnPropertyChanged(nameof(HideTip));
        OnPropertyChanged(nameof(HideBrush));
        if (_loaded) _ = ReloadAsync();
    }

    /// <summary>The sort button's tip: which way the table runs now, and what pressing it does.</summary>
    public string SortTip => SortIndex == 0 ? "Showing newest first. Click to show oldest first" : "Showing oldest first. Click to show newest first";

    /// <summary>The filters on the table's columns. They last until the page is left.</summary>
    public ColumnFilters Filters { get; } = new();

    // Every value each column has among the account's transactions, in order, for the lists in the column filters.
    private readonly Dictionary<FilterColumn, List<string>> _filterValues = new();

    /// <summary>The values to offer in a column's filter.</summary>
    public IReadOnlyList<string> FilterValues(FilterColumn column) =>
        _filterValues.TryGetValue(column, out var values) ? values : [];

    private void OnFiltersChanged()
    {
        OnPropertyChanged(nameof(HasFilters));
        if (_loaded) _ = ReloadAsync();
    }

    public bool HasFilters => Filters.Any || _drill is not null;

    /// <summary>
    /// Set when the page was opened from a figure on the budget: only the transactions that make up that figure are listed.
    /// It ends with the page, like the column filters.
    /// </summary>
    private TransactionDrillDown? _drill;

    [ObservableProperty] public partial string DrillText { get; set; } = "";

    public void ShowBehind(TransactionDrillDown? drill)
    {
        _drill = drill;
        DrillText = "";
        OnPropertyChanged(nameof(HasFilters));
        if (_loaded) _ = ReloadAsync();
    }

    public void ClearFilters()
    {
        var hadDrill = _drill is not null;
        _drill = null;
        DrillText = "";
        OnPropertyChanged(nameof(HasFilters));
        if (Filters.Any) Filters.Clear();   // this reloads the list
        else if (hadDrill && _loaded) _ = ReloadAsync();
    }

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
        OnPropertyChanged(nameof(SortTip));
        if (_loaded) _ = SortChangedAsync();
    }

    /// <summary>Raised once the rows are in their new order after the sort order was changed, so the page can show the right end of the table.</summary>
    public event Action? SortReloaded;

    private async Task SortChangedAsync()
    {
        try { await ReloadAsync(); }
        finally { SortReloaded?.Invoke(); }
    }

    partial void OnHideClearedChanged(bool value)
    {
        s_hideCleared = value;
        OnPropertyChanged(nameof(ClearedItemText));
        OnPropertyChanged(nameof(HideTip));
        OnPropertyChanged(nameof(HideBrush));
        if (_loaded) _ = ReloadAsync();
    }

    /// <summary>Set once the account's name, badge, accounts and drop-down lists have been loaded ahead of the rows, so the next load need not do them again.</summary>
    private bool _headerFresh;

    /// <summary>
    /// The quick first part of a load: the account's name and badge and the three figures at the top, with the lists the pickers need. The
    /// rows follow with <see cref="LoadAsync"/>, so the page can show all of this at once and only the table has to wait.
    /// </summary>
    public async Task LoadHeaderAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await LoadHeaderCoreAsync();
            await RefreshFiguresAsync();
            _headerFresh = true;
        }
        finally { _gate.Release(); }
    }

    public async Task LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_headerFresh) _headerFresh = false; else await LoadHeaderCoreAsync();
            _loaded = true;
            await LoadRowsAsync(null);
        }
        finally { _gate.Release(); }
    }

    private async Task LoadHeaderCoreAsync()
    {
            _accounts = await _store.GetAccountsAsync();
            var groups = await _store.GetGroupsAsync();
            var items = await _store.GetItemsAsync();
            await _store.EnsurePayeesFromTransactionsAsync();
            _hints = await _store.GetPayeeHintsAsync();
            await LoadPayeesAsync();

            PageTitle = AccountId is { } id ? _accounts.FirstOrDefault(a => a.Id == id)?.Name ?? "Account" : "All accounts";
            var viewed = _accounts.FirstOrDefault(a => a.Id == AccountId);
            ShowAccountBadge = viewed is not null;
            AccountBadgeText = viewed is { IsOnBudget: true } ? "Budget account" : "Tracking account";
            AccountBadgeIcon = viewed is null ? null : viewed.IsOnBudget ? Glyphs.BudgetAccountIcon() : Glyphs.TrackingAccountIcon();
            AccountBadgeTip = viewed is { IsOnBudget: true }
                ? "This account is part of your budget: its transactions count against your budget items."
                : "This account is only tracked: it sits outside your budget, so its transactions don't affect it.";
            // Both kinds of account get the same plain outlined label (it must not look like a button); only its words and their colour differ.
            AccountBadgeBrush = Format.Theme("CardBackgroundFillColorDefaultBrush");
            AccountBadgeBorderBrush = Format.Theme("ControlStrongStrokeColorDefaultBrush");
            AccountBadgeTextBrush = Format.Theme(viewed is { IsOnBudget: true } ? "AccentTextFillColorPrimaryBrush" : "SystemFillColorCautionBrush");   // the words are coloured to tell the two apart

            EntryAccounts.Clear();
            foreach (var a in _accounts.Where(a => !a.IsClosed || a.Id == AccountId))
                EntryAccounts.Add(new AccountOption(a.Id, a.IsClosed ? $"{a.Name} (closed)" : a.Name, a.IsOnBudget));

            RebuildItemOptions(groups, items);

            SyncColumnLayout();
            OnPropertyChanged(nameof(CanStartMonth));
    }

    private async Task LoadPayeesAsync()
    {
        Payees.Clear();
        PayeeIcons.Clear();
        foreach (var payee in await _store.GetPayeesAsync())
        {
            Payees.Add(payee.Name);
            if (!string.IsNullOrEmpty(payee.Emoji)) PayeeIcons[payee.Name] = payee.Emoji;
        }
    }

    private void RebuildItemOptions(IReadOnlyList<GroupEntity> groups, IReadOnlyList<ItemEntity> items)
    {
        ItemOptions.Clear();
        ItemOptions.Add(NoItem);
        foreach (var g in groups)
            foreach (var i in items.Where(i => i.GroupId == g.Id))
                ItemOptions.Add(new ItemOption(i.Id, $"{g.Name} › {i.DisplayName}", i.Kind, g.Id, i.Name,
                    $"{i.DisplayName}"));
    }

    /// <summary>Changes a counterparty's name (on all of its transactions) and icon, and updates what is on screen. Returns an error sentence, or null.</summary>
    /// <summary>How many transactions carry this counterparty's name.</summary>
    public async Task<int> CountPayeeUsesAsync(string name)
    {
        await _gate.WaitAsync();
        try { return (await _store.GetPayeeUsageAsync()).GetValueOrDefault(name.ToLower()); }
        finally { _gate.Release(); }
    }

    /// <summary>Deletes a counterparty that no transaction uses. Returns an error sentence, or null when it was deleted.</summary>
    public async Task<string?> DeletePayeeAsync(string name)
    {
        await _gate.WaitAsync();
        try
        {
            var payee = (await _store.GetPayeesAsync()).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (payee is null) return "That counterparty could not be found.";
            if (!await _store.DeletePayeeAsync(payee)) return $"{name} is used by transactions, so it can't be deleted.";
            await LoadPayeesAsync();
            _hints = await _store.GetPayeeHintsAsync();
            Message = "";
            return null;
        }
        catch (Exception ex) { return $"Could not delete: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    public async Task<string?> EditPayeeAsync(string oldName, string newName, string emoji)
    {
        await _gate.WaitAsync();
        try
        {
            var payee = (await _store.GetPayeesAsync()).FirstOrDefault(p => string.Equals(p.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (payee is null) return "That counterparty could not be found.";
            newName = newName.Trim();
            if (!string.Equals(payee.Name, newName, StringComparison.Ordinal))
            {
                var error = await _store.RenamePayeeAsync(payee, newName);
                if (error is not null) return error;
            }
            await _store.SetPayeeEmojiAsync(payee, emoji);

            await LoadPayeesAsync();
            _hints = await _store.GetPayeeHintsAsync();
            foreach (var row in Rows.OfType<TransactionRow>()) row.RefreshPayee();
            Message = "";
            return null;
        }
        catch (Exception ex) { return $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>Changes a budget item's name and icon and its group (which group it is in, and that group's name), and updates what is on screen. Returns an error sentence, or null.</summary>
    public async Task<string?> EditItemAsync(Guid itemId, string name, string emoji, Guid groupId, string groupName)
    {
        await _gate.WaitAsync();
        try
        {
            var error = await _store.UpdateItemAsync(itemId, name, emoji, groupId, groupName);
            RebuildItemOptions(await _store.GetGroupsAsync(), await _store.GetItemsAsync());
            await RefreshBalancesAsync();
            foreach (var row in Rows.OfType<TransactionRow>()) row.RefreshItem();
            if (error is null) Message = "";
            return error;
        }
        catch (Exception ex) { return $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    private async Task ReloadAsync(Guid? select = null)
    {
        await _gate.WaitAsync();
        try { await LoadRowsAsync(select); }
        finally { _gate.Release(); }
    }

    /// <summary>The three figures above the table: cleared, uncleared and available. Quick to work out, so they show before the rows do.</summary>
    private async Task RefreshFiguresAsync()
    {
        var balances = await _store.GetBalancesAsync(AccountId);
        AvailableText = Format.Money(balances.BookBalance);
        ClearedText = Format.Money(balances.ClearedBalance);
        UnclearedText = Format.Money(balances.UnclearedTotal);
    }

    /// <summary>Recalculates the balances shown at the top, and what is left on each budget item. The caller must hold the gate.</summary>
    private async Task RefreshBalancesAsync(IReadOnlyList<MonthSummary>? months = null)
    {
        await RefreshFiguresAsync();

        // What is left on each expense item this month, shown beside it in the item drop-down.
        months ??= BudgetEngine.Calculate(await _store.LoadSnapshotAsync());
        var remaining = months[^1].Items.ToDictionary(i => i.ItemId, i => i.Remaining);
        foreach (var option in ItemOptions)
            option.Balance = option.Id is { } id && remaining.TryGetValue(id, out var left) ? left : null;

        // The three items used most are listed first in the drop-down.
        var usage = await _store.GetItemUsageAsync();
        var top = usage.Where(u => u.Value > 0).OrderByDescending(u => u.Value).Take(3).Select(u => u.Key).ToHashSet();
        foreach (var option in ItemOptions)
        {
            option.UseCount = option.Id is { } used ? usage.GetValueOrDefault(used) : 0;
            option.IsFrequent = option.Id is { } topId && top.Contains(topId);
        }
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
        var showLines = (viewedAccount is null || viewedAccount.IsOnBudget) && !HasFilters;   // month lines have no place in a filtered list
        SyncColumnLayout();

        var accountNames = _accounts.ToDictionary(a => a.Id, a => a.Name);
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
        IReadOnlySet<Guid>? behind = null;
        if (_drill is { } drill)
        {
            behind = BudgetEngine.TransactionsBehind(snapshot, drill.MonthId, drill.ItemId);
            var itemName = snapshot.Items.FirstOrDefault(i => i.Id == drill.ItemId)?.Name ?? "";
            var monthLabel = months.FirstOrDefault(m => m.MonthId == drill.MonthId)?.Label ?? "";
            DrillText = $"{itemName}, {monthLabel}";
        }
        var filterSeen = Enum.GetValues<FilterColumn>().ToDictionary(c => c, _ => new Dictionary<string, IComparable>());
        foreach (var d in details)
        {
            if (Hidden(d.Transaction)) continue;
            if (behind is not null && !behind.Contains(d.Transaction.Id)) continue;
            var itemId = d.Lines.Count == 1 ? d.Lines[0].BudgetItemId : null;
            var option = itemId is { } id && itemsById.TryGetValue(id, out var found) ? found : NoItem;
            var otherSide = d.Transaction.TransferPairId is { } pair
                ? transferSides[pair].FirstOrDefault(t => t.TransactionId != d.Transaction.Id)
                : default;
            var row = new TransactionRow(d.Transaction, option, accountNames.GetValueOrDefault(d.Transaction.AccountId, "?"), d.Lines.Count, this)
            {
                OtherAccountId = otherSide.TransactionId == Guid.Empty ? null : otherSide.AccountId,
            };
            foreach (var column in Enum.GetValues<FilterColumn>())
            {
                var value = row.FilterValue(column);
                if (!filterSeen[column].ContainsKey(value)) filterSeen[column][value] = row.FilterOrder(column);
            }
            if (!Filters.Passes(row)) continue;
            entries.Add((new SortKey(d.Transaction.Date, d.Transaction.DayOrder), 1, d.Transaction.Id, row));
        }
        // With transactions hidden (cleared or reconciled ones, or the ones older than the stretch chosen), a month whose transactions are all hidden has nothing left under its line, so the line goes too.
        // (A month with no transactions at all keeps its line: nothing was hidden from it.)
        if ((HideCleared || HideReconciled || RangeIndex != 0) && showLines)
        {
            var lines = entries.Where(e => e.Row is MarkerRow).OrderBy(e => e.Key).ToList();
            var total = new int[lines.Count];
            var uncleared = new int[lines.Count];
            foreach (var d in details)
            {
                var key = new SortKey(d.Transaction.Date, d.Transaction.DayOrder);
                var at = -1;
                for (var i = 0; i < lines.Count && lines[i].Key.CompareTo(key) <= 0; i++) at = i;   // the last line at or before it: the month it is in
                if (at < 0) continue;
                total[at]++;
                if (!Hidden(d.Transaction)) uncleared[at]++;
            }
            var gone = lines.Where((_, i) => total[i] > 0 && uncleared[i] == 0).Select(l => l.Row).ToHashSet();
            entries.RemoveAll(e => gone.Contains(e.Row));
        }
        foreach (var (column, seen) in filterSeen)
            _filterValues[column] = seen.OrderBy(v => v.Value).ThenBy(v => v.Key, StringComparer.CurrentCultureIgnoreCase).Select(v => v.Key).ToList();

        var ordered = entries.OrderBy(e => e.Key).ThenBy(e => e.Rank).ThenBy(e => e.Id).Select(e => e.Row).ToList();
        if (SortIndex == 0) ordered.Reverse();

        // Rows that are unchanged stay as they are: only what really differs is removed, added or moved, so the table does not flash.
        var current = new Dictionary<string, object>();
        foreach (var row in Rows)
            if (row is not TransactionRow { IsNew: true } && KeyOf(row) is { } key) current.TryAdd(key, row);
        var final = ordered.Select(row => !_rebuildAll && KeyOf(row) is { } key && current.TryGetValue(key, out var old) && (old is TransactionRow { IsEditing: true } || SignatureOf(old) == SignatureOf(row)) ? old : row).ToList();   // a row being edited stays as it is, with what was typed in it
        _rebuildAll = false;

        var draft = _draft;
        _rebuilding = true;   // changing the list must not save or drop a new row that is being filled in
        object? toSelect = null;
        foreach (var row in final)
        {
            var id = row switch { TransactionRow t => t.Entity.Id, MarkerRow { Entity: { } m } => m.Id, _ => Guid.Empty };
            if (select is { } wanted && id == wanted) toSelect = row;
        }
        if (draft is not null && !draft.IsDirty) draft = null;
        if (draft is not null && toSelect is null)
        {
            if (SortIndex == 0) final.Insert(0, draft); else final.Add(draft);
            toSelect = draft;
        }
        else if (draft is not null) { draft.MarkDiscarded(); _draft = null; }

        var wantedRows = new HashSet<object>(final, ReferenceEqualityComparer.Instance);
        for (var i = Rows.Count - 1; i >= 0; i--)
            if (!wantedRows.Contains(Rows[i])) Rows.RemoveAt(i);
        for (var i = 0; i < final.Count; i++)
        {
            if (i < Rows.Count && ReferenceEquals(Rows[i], final[i])) continue;
            var at = Rows.IndexOf(final[i]);
            if (at > i) Rows.Move(at, i);
            else Rows.Insert(i, final[i]);
        }

        _editingRows.RemoveAll(r => !wantedRows.Contains(r));   // rows that are gone (deleted, or a new row that was saved) are no longer being edited
        OnPropertyChanged(nameof(IsRowEditing));
        SelectedEntry = toSelect;
        if (toSelect is TransactionRow { IsNew: true } unsaved) BeginEdit(unsaved);   // a new row being filled in stays open
        _rebuilding = false;
    }

    private static string? KeyOf(object row) => row switch
    {
        TransactionRow t => "t" + t.Entity.Id,
        MarkerRow m => "m" + (m.Entity?.Id.ToString() ?? "first"),
        _ => null,
    };

    /// <summary>Everything a row shows, so a row that has not changed can be kept rather than rebuilt.</summary>
    private static string SignatureOf(object row) => row switch
    {
        TransactionRow t => string.Join("|", t.Entity.Date, t.Entity.DayOrder, t.Entity.Payee, t.Entity.Memo, t.Entity.Amount, t.Entity.IsCleared, t.Entity.BankDate,
            t.Entity.AccountId, t.Entity.TransferPairId, t.Item.Id, t.LineCount, t.OtherAccountId, t.AccountName),
        MarkerRow m => string.Join("|", m.Label, m.LineBelow),
        _ => "",
    };

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
            }
            Message = note ?? "";

            // Clearing a row can make it drop out of a filtered list. A transfer's names and item depend on its accounts and amount.
            var disappears = Hidden(row.Entity);
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

    /// <summary>Saves the month the selected line stands for: the first month (where the budget starts) or the month of a later line.</summary>
    public async Task SetSelectedMarkerMonthAsync(DateOnly month)
    {
        if (SelectedMarker is not { } marker) return;
        await _gate.WaitAsync();
        try
        {
            Message = (marker.Entity is { } line ? await _store.SetMarkerMonthAsync(line, month) : await _store.SetFirstMonthAsync(month)) ?? "";
            await LoadRowsAsync(marker.Entity?.Id ?? Guid.Empty);   // also shows the old month again if it was refused
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>The label to suggest for a month starting on the given date: the month after the latest one.</summary>
    public string SuggestMonthLabel(DateTimeOffset date) => MonthLabels.Next(_lastMonthLabel, DateOnly.FromDateTime(date.DateTime));

    public async Task AddMonthAsync(DateOnly month)
    {
        await _gate.WaitAsync();
        try
        {
            var (line, error) = await _store.AddMonthAsync(month);
            Message = error ?? "";
            if (line is not null) await LoadRowsAsync(line.Id);
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
            var option = new ItemOption(item.Id, $"{group.Name} › {item.DisplayName}", item.Kind, group.Id, item.Name, item.DisplayName)
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
        if (account?.Id is not { } id) { Message = "Add an account first: use Add bank account in the left panel."; return null; }
        var other = transfer ? open.FirstOrDefault(a => a.Id != id) : null;
        if (transfer && other is null) { Message = "A transfer needs two accounts."; return null; }

        var entity = new TransactionEntity { AccountId = id, Date = DateOnly.FromDateTime(DateTime.Today), Payee = "" };
        if (transfer) entity.TransferPairId = Guid.CreateVersion7();   // marks it as a transfer until it is saved as a real pair
        var row = new TransactionRow(entity, NoItem, account.Name, 1, this) { IsNew = true, OtherAccountId = other?.Id };

        _draft = row;
        if (SortIndex == 0) Rows.Insert(0, row); else Rows.Add(row);   // the latest record: first when newest is first
        Message = "";
        SyncColumnLayout();
        BeginEdit(row);
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

        // A date that a month line sits on could be in either month, so ask which.
        int? dayOrder = null;
        var touchesBudget = (onBudget || (row.IsTransfer && row.OtherAccountId is { } other && IsOnBudget(other))) && !row.IsExpectedNow;   // an expected transaction is in no month yet
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
                var (from, to) = e.Amount <= 0 ? (e.AccountId, row.OtherAccountId!.Value) : (row.OtherAccountId!.Value, e.AccountId);   // with nothing typed, this account is the one paying
                var (outgoing, incoming) = await _store.AddTransferAsync(from, to, e.Date, Math.Abs(e.Amount),
                    string.IsNullOrWhiteSpace(e.Memo) ? null : e.Memo.Trim(), row.Item.Id, dayOrder);
                outgoing.IsUndated = incoming.IsUndated = e.IsUndated;
                var mine = e.Amount <= 0 ? outgoing : incoming;
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
    public bool RequiresItem(TransactionRow row) => false;   // an item is never required; a split can leave lines unassigned too

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

    // ---- Ticking transactions, to delete them together ----
    /// <summary>Selection mode: tick boxes show beside the transactions, so several can be ticked and deleted together. Entered from the right-click menu's Select.</summary>
    [ObservableProperty] public partial bool IsSelecting { get; set; }

    partial void OnIsSelectingChanged(bool value)
    {
        ColumnLayout.Current.Selecting = value;
        if (!value) SetAllTicked(false);
        else TicksChanged();
    }


    /// <summary>The ticked transactions (a new row that is not saved yet is never counted).</summary>
    public List<TransactionRow> TickedRows() => Rows.OfType<TransactionRow>().Where(r => r.IsTicked && !r.IsNew).ToList();

    /// <summary>How many transactions are ticked, and whether every one in the table is.</summary>
    public event EventHandler? TickedChanged;

    private bool _tickingAll;

    public void TicksChanged()
    {
        if (!_tickingAll) TickedChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAllTicked(bool ticked)
    {
        _tickingAll = true;
        try { foreach (var row in Rows.OfType<TransactionRow>()) if (!row.IsNew) row.IsTicked = ticked; }
        finally { _tickingAll = false; }
        TickedChanged?.Invoke(this, EventArgs.Empty);
    }

    public int TransactionCount => Rows.OfType<TransactionRow>().Count(r => !r.IsNew);

    /// <summary>Deletes all the given transactions in one go (one Ctrl+Z brings them back).</summary>
    public async Task DeleteManyAsync(IReadOnlyList<TransactionRow> rows)
    {
        var saved = rows.Where(r => !r.IsNew).Select(r => r.Entity).ToList();
        if (saved.Count == 0) return;
        await _gate.WaitAsync();
        try
        {
            await _store.DeleteTransactionsAsync(saved);
            await LoadRowsAsync(null);
        }
        catch (Exception ex) { Message = $"Could not delete: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Duplicate: a new row, filled in like the given one (same account, counterparty, memo, budget item or split, and amount) but dated today, opens for editing
    /// as the latest entry in the table. It is saved like any new row, with Enter or the tick, and goes away on Esc.
    /// </summary>
    public async Task BeginDuplicateAsync(TransactionRow row)
    {
        if (_draft is { } existing) { SelectedEntry = existing; return; }

        var source = row.Entity;
        var entity = new TransactionEntity { AccountId = source.AccountId, Date = DateOnly.FromDateTime(DateTime.Today), Payee = source.Payee, Memo = source.Memo, Amount = source.Amount };
        if (row.IsTransfer) entity.TransferPairId = Guid.CreateVersion7();   // marks it as a transfer until it is saved as a real pair
        var copy = new TransactionRow(entity, row.Item, row.AccountName, row.LineCount, this) { IsNew = true, OtherAccountId = row.OtherAccountId };
        if (row.IsSplit)
        {
            var lines = await GetLinesAsync(row);
            copy.SetDraftSplits(lines.Select(l => (l.BudgetItemId, l.Amount, l.Memo)).ToList());
        }

        _draft = copy;
        if (SortIndex == 0) Rows.Insert(0, copy); else Rows.Add(copy);   // the latest entry in the table: first when newest is first, as for any new row
        Message = "";
        SyncColumnLayout();
        BeginEdit(copy);
    }

    /// <summary>What importing the lines would do, without saving anything.</summary>
    public Task<TransactionImportResult> PreviewImportAsync(IEnumerable<TransactionImportRow> rows) => _store.ImportTransactionsAsync(AccountId, rows, apply: false);

    public async Task<TransactionImportResult?> ImportAsync(IEnumerable<TransactionImportRow> rows)
    {
        await _gate.WaitAsync();
        try
        {
            var result = await _store.ImportTransactionsAsync(AccountId, rows);
            _hints = await _store.GetPayeeHintsAsync();
            await LoadRowsAsync(null);
            return result;
        }
        catch (Exception ex) { Message = $"The import failed: {ex.Message}"; return null; }
        finally { _gate.Release(); }
    }

    public Task ExportAsync(string path) => _store.ExportTransactionsAsync(path, AccountId);

    // ---- Importing a bank file: the wizard's reconciliation ----

    /// <summary>The accounts a file of transactions can go into: open accounts kept by transactions.</summary>
    public List<AccountOption> ImportAccounts() => _accounts.Where(a => !a.IsClosed && !a.TrackBalances).Select(a => new AccountOption(a.Id, a.Name, a.IsOnBudget)).ToList();

    public bool AccountIsOnBudget(Guid? id) => _accounts.FirstOrDefault(a => a.Id == id)?.IsOnBudget == true;

    public Task<BankFormat?> GetBankFormatAsync(Guid accountId) => _store.GetBankFormatAsync(accountId);

    public Task<bool> GetBankSkipSetupAsync(Guid accountId) => _store.GetBankSkipSetupAsync(accountId);

    // ---- Finding why an import does not reconcile ----

    public Task<BalanceDiagnosis?> DiagnoseBalanceAsync(Guid accountId, IReadOnlyList<BankLine> lines, (DateOnly Date, long Balance)? statement = null) => _store.DiagnoseBalanceAsync(accountId, lines, statement);

    public Task<StatementSettings?> GetStatementSettingsAsync(Guid accountId) => _store.GetStatementSettingsAsync(accountId);

    /// <summary>The figures the file gave against the account on the file's date.</summary>
    public Task<IReadOnlyList<BalanceComparison>> CompareStatementAsync(Guid accountId, StatementInfo info) => _store.CompareStatementAsync(accountId, info);

    public Task<long> GetOpeningBalanceAsync(Guid accountId) => _store.GetOpeningBalanceAsync(accountId);

    /// <summary>Makes one change to fix a difference, then reloads the table and the balances.</summary>
    public async Task FixAsync(Func<BudgetStore, Task> change)
    {
        await _gate.WaitAsync();
        try
        {
            await change(_store);
            _hints = await _store.GetPayeeHintsAsync();
            _rebuildAll = true;
            await LoadRowsAsync(null);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Like <see cref="FixAsync"/>, for a change that can be refused (false): the table is reloaded either way.</summary>
    public async Task<bool> TryFixAsync(Func<BudgetStore, Task<bool>> change)
    {
        await _gate.WaitAsync();
        try
        {
            var done = await change(_store);
            _hints = await _store.GetPayeeHintsAsync();
            _rebuildAll = true;
            await LoadRowsAsync(null);
            return done;
        }
        finally { _gate.Release(); }
    }

    /// <summary>The cleared balance of an account as at a date (the figure the account screen shows, for that day).</summary>
    public async Task<long> ClearedBalanceAsOfAsync(Guid accountId, DateOnly date) => (await _store.GetBalancesAsync(accountId, default, date)).ClearedBalance;

    /// <summary>The available balance (cleared and uncleared together) of an account as at a date.</summary>
    public async Task<long> AvailableBalanceAsOfAsync(Guid accountId, DateOnly date) => (await _store.GetBalancesAsync(accountId, default, date)).BookBalance;

    public IReadOnlyDictionary<string, string> PayeeIconsForImport => PayeeIcons;

    public Task<IReadOnlyList<BankMatch>> MatchBankLinesAsync(Guid accountId, IReadOnlyList<BankLine> lines) => _store.MatchBankLinesAsync(accountId, lines);

    /// <summary>Clears the matched transactions and adds the lines that were not in the table, in one save, and remembers the file layout for the account.</summary>
    public async Task<(int Cleared, int Added)?> ApplyBankMatchesAsync(Guid accountId, IReadOnlyList<BankClear> clear, IReadOnlyList<BankLine> add, BankFormat format, bool skipSetup, StatementSettings statement)
    {
        await _gate.WaitAsync();
        try
        {
            var result = await _store.ApplyBankMatchesAsync(accountId, clear, add);
            await _store.SaveBankFormatAsync(accountId, format);
            await _store.SetBankSkipSetupAsync(accountId, skipSetup);
            await _store.SaveStatementSettingsAsync(accountId, statement);
            _hints = await _store.GetPayeeHintsAsync();
            _rebuildAll = true;
            await LoadRowsAsync(null);
            Message = $"Import: {result.Cleared} reconciled, {result.Added} added.";
            return result;
        }
        catch (Exception ex) { Message = $"The import failed: {ex.Message}"; return null; }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(TransactionRow row)
    {
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
