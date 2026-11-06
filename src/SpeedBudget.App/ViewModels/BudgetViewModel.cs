using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SpeedBudget.Core;
using SpeedBudget.Data;

namespace SpeedBudget.App.ViewModels;

/// <summary>An expense group that can be folded away. Shared by the name column and every month column.</summary>
public sealed partial class GroupState(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty] public partial bool IsExpanded { get; set; } = true;
}

/// <summary>
/// One row of the budget, as labelled in the left-hand column. Every month column has a cell for each line, in the
/// same order, so the rows line up across the page.
/// </summary>
public sealed partial class BudgetLine(Guid? itemId, string name, int depth, bool isArchived, GroupState? group, bool isHeader,
    Guid? groupId = null, Guid? parentItemId = null) : ObservableObject
{
    public Guid? ItemId { get; } = itemId;
    public Guid? GroupId { get; } = groupId;
    public Guid? ParentItemId { get; } = parentItemId;
    public string Name { get; } = name;
    public Thickness Indent { get; } = new(depth * 20, 0, 0, 0);
    public bool IsArchived { get; } = isArchived;
    public GroupState? Group { get; } = group;
    public bool IsHeader { get; } = isHeader;
    public bool IsItem => !IsHeader;

    /// <summary>For a group heading: the items it totals.</summary>
    public List<Guid> GroupItemIds { get; } = new();

    [ObservableProperty] public partial bool IsVisible { get; set; } = true;

    /// <summary>True for a few seconds after the user is taken to this item, so it can be found on a long page.</summary>
    [ObservableProperty] public partial bool IsHighlighted { get; set; }

    public Brush HighlightBrush => IsHighlighted ? Format.Highlight : IsHeader ? Format.GroupHeader : Format.Clear;

    partial void OnIsHighlightedChanged(bool value) => OnPropertyChanged(nameof(HighlightBrush));
}

/// <summary>Base for a month's cell on a line: it shows or hides together with its line.</summary>
public abstract partial class LineCell : ObservableObject
{
    public BudgetLine Line { get; }

    protected LineCell(BudgetLine line)
    {
        Line = line;
        line.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BudgetLine.IsVisible)) OnPropertyChanged(nameof(IsVisible));
        };
    }

    public bool IsVisible => Line.IsVisible;
}

public sealed partial class IncomeCell : LineCell
{
    public Guid MonthId { get; }
    public Guid ItemId { get; }
    public string Name => Line.Name;
    public long ReceivedCents { get; private set; }
    public long HeldCents { get; private set; }

    /// <summary>What is still available to budget this month: what arrived, less what is held for next month.</summary>
    public long AvailableCents => ReceivedCents - HeldCents;

    [ObservableProperty] public partial string ReceivedText { get; set; } = "";
    [ObservableProperty] public partial string AvailableText { get; set; } = "";
    [ObservableProperty] public partial bool CanHold { get; set; }
    [ObservableProperty] public partial bool CanRelease { get; set; }

    public IncomeCell(BudgetLine line, Guid monthId) : base(line)
    {
        MonthId = monthId;
        ItemId = line.ItemId!.Value;
    }

    public void Apply(IncomeItemMonth m)
    {
        ReceivedCents = m.Received;
        HeldCents = m.Held;
        ReceivedText = Format.Money(m.Received);
        AvailableText = Format.Money(AvailableCents);
        CanHold = AvailableCents > 0;
        CanRelease = m.Held > 0;
    }
}

public sealed partial class OutflowCell : LineCell
{
    private readonly BudgetViewModel _owner;
    private long _budgeted;

    public Guid MonthId { get; }

    public long BudgetedCents => _budgeted;

    /// <summary>What is left on the item this month, including anything carried forward. Green when above zero, red below.</summary>
    public long LeftCents { get; private set; }

    public OutflowCell(BudgetLine line, Guid monthId, BudgetViewModel owner) : base(line)
    {
        MonthId = monthId;
        _owner = owner;
    }

    /// <summary>The editable budgeted amount, in rands. Only items have one; group headings show totals.</summary>
    public double Budgeted
    {
        get => (double)AppServices.ToRands(_budgeted);
        set
        {
            if (Line.ItemId is not { } itemId) return;
            var cents = AppServices.ToCents(value);
            if (cents == _budgeted) return;
            _budgeted = cents;
            OnPropertyChanged();
            _ = _owner.AllocationEditedAsync(MonthId, itemId, cents);
        }
    }

    [ObservableProperty] public partial string BudgetedText { get; set; } = "";
    [ObservableProperty] public partial string SpentText { get; set; } = "";
    [ObservableProperty] public partial string LeftText { get; set; } = "";
    [ObservableProperty] public partial Brush LeftBrush { get; set; } = Format.Theme("TextFillColorPrimaryBrush");

    /// <summary>Explains where part of the Left figure came from, when some was carried forward. Null when nothing was.</summary>
    [ObservableProperty] public partial string? LeftTip { get; set; }

    public void Apply(long budgeted, long activity, long carriedIn)
    {
        _budgeted = budgeted;
        OnPropertyChanged(nameof(Budgeted));
        var left = carriedIn + budgeted + activity;
        LeftCents = left;
        LeftTip = carriedIn > 0 ? $"Includes {Format.Money(carriedIn)} carried forward from last month" : null;
        BudgetedText = Format.Money(budgeted);
        SpentText = Format.Money(-activity);
        LeftText = Format.Money(left);
        LeftBrush = Format.Signed(left);
    }
}

/// <summary>A label in the summary block. The emphasised one is taller, to match its larger figures.</summary>
/// <summary>Whether the monthly figures above "Available to budget" are showing. They are open at the top of the page and fold away as it is scrolled.</summary>
public static class SummaryView
{
    public static bool Expanded { get; set; } = true;

    /// <summary>The arrow on the "Available to budget" row: down while the figures are hidden (they open downwards), up while they show.</summary>
    public static string Arrow(bool expanded) => expanded ? "\uE70E" : "\uE70D";
}

public sealed partial class SummaryLabel(string text, bool isStrong = false) : ObservableObject
{
    public string Text { get; } = text;
    public bool IsStrong { get; } = isStrong;

    /// <summary>Only "Available to budget" is always visible; the rest show while the figures are expanded.</summary>
    [ObservableProperty] public partial bool IsVisible { get; set; } = isStrong || SummaryView.Expanded;

    /// <summary>The arrow on the "Available to budget" row that shows or hides the other figures.</summary>
    [ObservableProperty] public partial string ToggleGlyph { get; set; } = SummaryView.Arrow(SummaryView.Expanded);
}

public sealed partial class SummaryCell(bool isStrong) : ObservableObject
{
    public bool IsStrong { get; } = isStrong;

    /// <summary>For "Available to budget": the month it belongs to and the amount (negative when overspent).</summary>
    public Guid MonthId { get; set; }
    public long Cents { get; set; }

    [ObservableProperty] public partial bool IsVisible { get; set; } = isStrong || SummaryView.Expanded;

    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial Brush Brush { get; set; } = Format.Theme("TextFillColorPrimaryBrush");
}

/// <summary>One month shown as a column: a summary block, then cash inflow and cash outflow.</summary>
public sealed partial class MonthColumn : ObservableObject
{
    public Guid MonthId { get; }
    public string Label { get; private set; } = "";

    /// <summary>A month after the latest real one: it has no transactions yet, only a budget being planned.</summary>
    public bool IsPlanned { get; private set; }
    public bool HasPrevious { get; }

    /// <summary>How wide this month is drawn: wider when it is the only month on show.</summary>
    public double Width { get; }
    public IReadOnlyList<SummaryCell> Summary { get; }

    /// <summary>The "Available to budget" cell, which stays in view under the month heading while the other figures are hidden.</summary>
    public SummaryCell AvailableCell => Summary[6];

    /// <summary>Whether the summary block (all of its figures) is showing in the scrolling part of the page.</summary>
    [ObservableProperty] public partial bool SummaryOpen { get; set; } = SummaryView.Expanded;
    public ObservableCollection<IncomeCell> Income { get; } = new();
    public ObservableCollection<OutflowCell> Outflow { get; } = new();

    public MonthColumn(Guid monthId, bool hasPrevious, IReadOnlyList<BudgetLine> incomeLines, IReadOnlyList<BudgetLine> outflowLines, BudgetViewModel owner, double width)
    {
        MonthId = monthId;
        HasPrevious = hasPrevious;
        Width = width;
        Summary = Enumerable.Range(0, 7).Select(i => new SummaryCell(isStrong: i == 6) { MonthId = monthId }).ToList();
        foreach (var line in incomeLines) Income.Add(new IncomeCell(line, monthId));
        foreach (var line in outflowLines) Outflow.Add(new OutflowCell(line, monthId, owner));
    }

    public void Apply(MonthSummary m)
    {
        Label = m.Label;
        IsPlanned = m.IsPlanned;
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(IsPlanned));

        var values = new[] { m.Income, -m.HeldOut, m.HeldIn, m.Overflow, m.CarriedOver, -m.Budgeted, m.Available };
        for (var i = 0; i < values.Length; i++) Summary[i].Text = Format.Money(values[i]);
        Summary[6].Brush = Format.Signed(m.Available);
        Summary[6].Cents = m.Available;

        var income = m.IncomeItems.ToDictionary(i => i.ItemId);
        foreach (var cell in Income) cell.Apply(income.GetValueOrDefault(cell.ItemId) ?? new IncomeItemMonth(cell.ItemId, 0, 0, 0));

        var items = m.Items.ToDictionary(i => i.ItemId);
        foreach (var cell in Outflow)
        {
            if (cell.Line.IsHeader) continue;   // group headings carry no figures

            var item = items.GetValueOrDefault(cell.Line.ItemId!.Value);
            cell.Apply(item?.Budgeted ?? 0, item?.Activity ?? 0, item?.CarriedIn ?? 0);
        }
    }
}

public sealed partial class BudgetViewModel : ObservableObject
{
    /// <summary>The labels down the left of the summary block, in the order of each month's summary cells.</summary>
    public static SummaryLabel AvailableLabel => SummaryLabels[6];

    public static readonly IReadOnlyList<SummaryLabel> SummaryLabels = new[]
    {
        new SummaryLabel("Income this month"), new SummaryLabel("Held for next month"), new SummaryLabel("Released from last month"),
        new SummaryLabel("Overflow from last month"), new SummaryLabel("Carried over"), new SummaryLabel("Budgeted"),
        new SummaryLabel("Available to budget", isStrong: true),
    };

    private readonly BudgetStore _store = AppServices.Store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<MonthSummary> _months = Array.Empty<MonthSummary>();
    private readonly List<BudgetLine> _incomeLines = new();
    private readonly List<BudgetLine> _outflowLines = new();
    private int _start;
    private int _fitCapacity = 1;
    private bool _firstLoad = true;

    public ObservableCollection<BudgetLine> IncomeLines { get; } = new();
    public ObservableCollection<BudgetLine> OutflowLines { get; } = new();
    public ObservableCollection<MonthColumn> Columns { get; } = new();

    /// <summary>Shows or hides the monthly figures above "Available to budget", in every month and in the labels.</summary>
    public void SetSummaryExpanded(bool expanded)
    {
        SummaryView.Expanded = expanded;
        SummaryOpen = expanded;
        foreach (var column in Columns) column.SummaryOpen = expanded;
        foreach (var label in SummaryLabels)
        {
            if (!label.IsStrong) label.IsVisible = expanded;
            else label.ToggleGlyph = SummaryView.Arrow(expanded);
        }
        foreach (var column in Columns)
            foreach (var cell in column.Summary.Where(c => !c.IsStrong)) cell.IsVisible = expanded;
    }

    /// <summary>True while the summary block is showing; when it is hidden "Available to budget" is frozen above the table instead.</summary>
    [ObservableProperty] public partial bool SummaryOpen { get; set; } = SummaryView.Expanded;

    public bool SummaryClosed => !SummaryOpen;

    partial void OnSummaryOpenChanged(bool value) => OnPropertyChanged(nameof(SummaryClosed));

    /// <summary>Which column titles are frozen under the month headings: 0 none, 1 those of the cash inflow rows, 2 those of the cash outflow rows.</summary>
    [ObservableProperty] public partial int FrozenTitles { get; set; }

    public bool FrozenInflow => FrozenTitles == 1;
    public bool FrozenOutflow => FrozenTitles == 2;

    partial void OnFrozenTitlesChanged(int value)
    {
        OnPropertyChanged(nameof(FrozenInflow));
        OnPropertyChanged(nameof(FrozenOutflow));
    }

    [ObservableProperty] public partial bool CanGoBack { get; set; }
    [ObservableProperty] public partial bool CanGoForward { get; set; }

    /// <summary>True when the current calendar month is in the budget but is not the leftmost column on show.</summary>
    [ObservableProperty] public partial bool ShowJumpToCurrent { get; set; }
    [ObservableProperty] public partial bool HasItems { get; set; }
    [ObservableProperty] public partial string RangeText { get; set; } = "";
    [ObservableProperty] public partial string Message { get; set; } = "";

    public bool HasMessage => Message != "";

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    /// <summary>How many month columns are shown: one, or as many as fit across the page.</summary>
    private int _capacity => ShowSingleMonth ? 1 : _fitCapacity;

    private int MaxStart => Math.Max(0, _months.Count - _capacity);

    /// <summary>True to see one month at a time, which leaves room for longer item names.</summary>
    [ObservableProperty] public partial bool ShowSingleMonth { get; set; }

    public bool ShowManyMonths => !ShowSingleMonth;

    partial void OnShowSingleMonthChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowManyMonths));
    }

    /// <summary>Switches between one month and many, remembering the choice for next time.</summary>
    public async Task SetSingleMonthAsync(bool single)
    {
        if (ShowSingleMonth == single) return;
        ShowSingleMonth = single;
        _start = Math.Clamp(_start, 0, MaxStart);
        RebuildColumns();
        try { await _store.SetLocalSettingAsync(ViewModeKey, single ? "single" : "many"); }
        catch (Exception ex) { Message = $"Could not remember the view: {ex.Message}"; }
    }

    private const string ViewModeKey = "budgetView";

    /// <summary>The width of a month when several are shown side by side, and when only one is.</summary>
    public const double MonthWidth = 330;
    public const double SingleMonthWidth = 660;

    public async Task LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var groups = await _store.GetGroupsAsync();
            var items = await _store.GetItemsAsync();

            _incomeLines.Clear();
            _outflowLines.Clear();
            foreach (var group in groups)
            {
                var inGroup = items.Where(i => i.GroupId == group.Id).ToList();
                if (group.Kind == ItemKind.Income)
                {
                    foreach (var (item, depth) in Flatten(inGroup))
                        _incomeLines.Add(new BudgetLine(item.Id, item.DisplayName, depth, item.IsArchived, null, isHeader: false, item.GroupId, item.ParentItemId));
                    continue;
                }

                var state = new GroupState(group.Name);
                state.PropertyChanged += (_, _) => ApplyVisibility();
                var header = new BudgetLine(null, group.Name, 0, false, state, isHeader: true, group.Id);
                _outflowLines.Add(header);
                foreach (var (item, depth) in Flatten(inGroup))
                {
                    _outflowLines.Add(new BudgetLine(item.Id, item.DisplayName, depth, item.IsArchived, state, isHeader: false, item.GroupId, item.ParentItemId));
                    header.GroupItemIds.Add(item.Id);
                }
            }

            IncomeLines.Clear();
            foreach (var l in _incomeLines) IncomeLines.Add(l);
            OutflowLines.Clear();
            foreach (var l in _outflowLines) OutflowLines.Add(l);

            HasItems = items.Count > 0;
            if (_firstLoad) ShowSingleMonth = await _store.GetLocalSettingAsync(ViewModeKey) == "single";
            _months = BudgetEngine.Calculate(await _store.LoadSnapshotAsync(), BudgetEngine.PlannedMonthCount);

            // Open on the latest real month, with the planned months after it, rather than on the far future.
            if (_firstLoad)
            {
                // Open on the current calendar month when the budget has one, else on the latest month that has a line.
                var current = MonthLabels.For(DateOnly.FromDateTime(DateTime.Today));
                var list = _months.ToList();
                var index = list.FindIndex(m => m.Label == current);
                _start = index >= 0 ? index : list.FindLastIndex(m => !m.IsPlanned);
                _firstLoad = false;
            }
            _start = Math.Clamp(_start, 0, MaxStart);
            RebuildColumns();
        }
        finally { _gate.Release(); }
    }

    /// <summary>Items depth-first, so sub-items follow their parent and carry how deep they are.</summary>
    private static IEnumerable<(ItemEntity Item, int Depth)> Flatten(List<ItemEntity> items, Guid? parent = null, int depth = 0)
    {
        foreach (var item in items.Where(i => i.ParentItemId == parent))
        {
            yield return (item, depth);
            foreach (var child in Flatten(items, item.Id, depth + 1)) yield return child;
        }
    }

    /// <summary>How many month columns fit across the page. Called when the page is resized.</summary>
    public void SetCapacity(int capacity)
    {
        capacity = Math.Max(1, capacity);
        if (capacity == _fitCapacity) return;
        _fitCapacity = capacity;
        if (_months.Count == 0 || ShowSingleMonth) return;

        // Keep the latest month in view when the window grows.
        _start = Math.Clamp(_start, 0, MaxStart);
        RebuildColumns();
    }

    /// <summary>Shows earlier months. At the very first month it adds an empty month before it, so the budget can go back in time.</summary>
    public async Task GoBackAsync()
    {
        if (_start > 0)
        {
            _start--;
            RebuildColumns();
            return;
        }
        if (!CanAddEarlierMonth || await _store.AddEarlierMonthAsync() is null) return;
        await LoadAsync();
    }

    private bool CanAddEarlierMonth =>
        _months.Count == 0 || !MonthLabels.TryParse(_months[0].Label, out var first) || first.AddMonths(-1) >= BudgetStore.EarliestMonth;

    public void GoForward()
    {
        if (_start >= MaxStart) return;
        _start++;
        RebuildColumns();
    }

    private static string CurrentMonthLabel => MonthLabels.For(DateOnly.FromDateTime(DateTime.Today));

    private int IndexOfMonth(string label) => _months.ToList().FindIndex(m => m.Label == label);

    /// <summary>Brings the current calendar month to the leftmost column. False if the budget has no such month.</summary>
    public bool JumpToCurrentMonth() => JumpToMonth(DateOnly.FromDateTime(DateTime.Today));

    /// <summary>Brings the given calendar month to the leftmost column. False if the budget does not (yet) include that month.</summary>
    public bool JumpToMonth(DateOnly month)
    {
        var index = IndexOfMonth(MonthLabels.For(month));
        if (index < 0) return false;
        _start = Math.Clamp(index, 0, MaxStart);
        RebuildColumns();
        return true;
    }

    /// <summary>The month at the left of the screen, which the month picker starts from.</summary>
    public DateOnly? LeftmostMonth => Columns.Count > 0 && MonthLabels.TryParse(Columns[0].Label, out var month) ? month : null;

    private void RebuildColumns()
    {
        Columns.Clear();
        for (var i = _start; i < Math.Min(_months.Count, _start + _capacity); i++)
            Columns.Add(new MonthColumn(_months[i].MonthId, hasPrevious: i > 0, _incomeLines, _outflowLines, this, ShowSingleMonth ? SingleMonthWidth : MonthWidth));
        ApplyData();
    }

    /// <summary>Pushes the calculated numbers into the existing cells, so editing never disturbs focus.</summary>
    private void ApplyData()
    {
        foreach (var column in Columns)
            column.Apply(_months.First(m => m.MonthId == column.MonthId));

        CanGoBack = _start > 0 || CanAddEarlierMonth;
        CanGoForward = _start < MaxStart;
        var currentAt = IndexOfMonth(CurrentMonthLabel);
        ShowJumpToCurrent = currentAt >= 0 && _start != Math.Clamp(currentAt, 0, MaxStart);
        RangeText = Columns.Count == 0 ? "" : Columns.Count == 1 ? Columns[0].Label : $"{Columns[0].Label} to {Columns[^1].Label}";
        ApplyVisibility();
    }

    /// <summary>
    /// Archived items only show while some visible month has numbers on them, and folded groups hide their items.
    /// Every column hides the same lines, so the rows keep lining up.
    /// </summary>
    private void ApplyVisibility()
    {
        var shown = Columns.Select(c => _months.First(m => m.MonthId == c.MonthId)).ToList();
        var withNumbers = new HashSet<Guid>();
        foreach (var m in shown)
        {
            foreach (var i in m.Items.Where(i => i.Budgeted != 0 || i.Activity != 0)) withNumbers.Add(i.ItemId);
            foreach (var i in m.IncomeItems.Where(i => i.Received != 0 || i.Held != 0)) withNumbers.Add(i.ItemId);
        }

        bool Relevant(BudgetLine line) => !line.IsArchived || withNumbers.Contains(line.ItemId!.Value);

        foreach (var line in _incomeLines) line.IsVisible = Relevant(line);

        foreach (var line in _outflowLines.Where(l => l.IsItem))
            line.IsVisible = Relevant(line) && line.Group!.IsExpanded;
        foreach (var header in _outflowLines.Where(l => l.IsHeader))
            header.IsVisible = !_outflowLines.Any(l => l.IsItem && l.Group == header.Group) || _outflowLines.Any(l => l.IsItem && l.Group == header.Group && Relevant(l));
    }

    private async Task RecalculateAsync()
    {
        _months = BudgetEngine.Calculate(await _store.LoadSnapshotAsync(), BudgetEngine.PlannedMonthCount);
        _start = Math.Clamp(_start, 0, MaxStart);
        var wanted = _months.Skip(_start).Take(_capacity).Select(m => m.MonthId).ToList();
        if (wanted.SequenceEqual(Columns.Select(c => c.MonthId))) ApplyData();
        else RebuildColumns();
    }

    public async Task<ItemEntity?> GetItemAsync(Guid itemId) => (await _store.GetItemsAsync()).FirstOrDefault(i => i.Id == itemId);

    /// <summary>Adds an expense group (shown on the page with no items yet).</summary>
    public async Task AddGroupAsync(string name)
    {
        await _store.AddGroupAsync(name.Trim(), ItemKind.Expense);
        await LoadAsync();
    }

    /// <summary>Adds an item to a group; for income without a group given, to the first income group (made if there is none).</summary>
    public async Task AddItemAsync(Guid? groupId, ItemKind kind, string name, string? emoji)
    {
        if (groupId is null)
        {
            var existing = (await _store.GetGroupsAsync()).FirstOrDefault(g => g.Kind == kind);
            groupId = (existing ?? await _store.AddGroupAsync(kind == ItemKind.Income ? "Income" : "Expenses", kind)).Id;
        }
        var item = await _store.AddItemAsync(groupId.Value, null, name.Trim(), kind);
        if (!string.IsNullOrEmpty(emoji)) await _store.EditItemAsync(item.Id, null, emoji);
        await LoadAsync();
    }

    public async Task RenameGroupAsync(Guid groupId, string name)
    {
        await _store.RenameGroupAsync(groupId, name);
        await LoadAsync();
    }

    public async Task EditItemAsync(Guid itemId, string? name, string? emoji)
    {
        await _store.EditItemAsync(itemId, name, emoji);
        await LoadAsync();
    }

    /// <summary>Saves a budgeted amount typed into a month.</summary>
    public async Task AllocationEditedAsync(Guid monthId, Guid itemId, long cents)
    {
        await _gate.WaitAsync();
        try
        {
            await _store.SetAllocationAsync(monthId, itemId, cents);
            Message = "";
            await RecalculateAsync();
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>The budget items of a month that budget can be moved to or from, with what is left on each. Archived items are left out.</summary>
    public List<Views.BudgetChoice> ChoicesFor(Guid monthId, Guid? excluding = null, bool onlyWithMoneyLeft = false)
    {
        var column = Columns.FirstOrDefault(c => c.MonthId == monthId);
        if (column is null) return [];
        return column.Outflow
            .Where(c => c.Line.IsItem && !c.Line.IsArchived && c.Line.ItemId != excluding && (!onlyWithMoneyLeft || c.LeftCents > 0))
            .Select(c => new Views.BudgetChoice(c.Line.ItemId!.Value, c.Line.Name, c.LeftCents))
            .ToList();
    }

    /// <summary>Changes what is budgeted on several items of a month at once (the amounts are added, so a negative one takes budget away).</summary>
    public async Task MoveBudgetAsync(Guid monthId, params (Guid ItemId, long Delta)[] deltas)
    {
        await _gate.WaitAsync();
        try
        {
            await _store.AdjustAllocationsAsync(monthId, deltas);
            Message = "";
            await RecalculateAsync();
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>Holds more of this month's income for next month. It can't be more than is still available.</summary>
    public async Task HoldAsync(IncomeCell cell, long cents)
    {
        if (cents <= 0) return;
        await _gate.WaitAsync();
        try
        {
            var held = Math.Min(cell.HeldCents + cents, Math.Max(0, cell.ReceivedCents));
            await _store.SetIncomeHoldAsync(cell.MonthId, cell.ItemId, held);
            Message = held - cell.HeldCents == cents ? ""
                : $"Only {Format.Money(cell.AvailableCents)} of {cell.Name} was still available, so that is all that was held.";
            await RecalculateAsync();
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>Makes everything held back available to budget this month again.</summary>
    public async Task ReleaseAsync(IncomeCell cell)
    {
        await _gate.WaitAsync();
        try
        {
            await _store.SetIncomeHoldAsync(cell.MonthId, cell.ItemId, 0);
            Message = "";
            await RecalculateAsync();
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Brings an item into view for the page: opens its group if folded, and highlights its row for a few seconds.
    /// Returns the line, or null if the item is not on the budget.
    /// </summary>
    public BudgetLine? Reveal(Guid itemId)
    {
        var line = _outflowLines.FirstOrDefault(l => l.ItemId == itemId);
        if (line is null) return null;

        if (line.Group is { IsExpanded: false } group) group.IsExpanded = true;
        line.IsHighlighted = true;
        _ = ClearHighlightLaterAsync(line);
        return line;
    }

    private static async Task ClearHighlightLaterAsync(BudgetLine line)
    {
        await Task.Delay(3500);
        line.IsHighlighted = false;
    }

    /// <summary>
    /// Moves a budget item to where it was dropped on the budget: before or after another item (taking that item's
    /// category and parent), or to the top of a category when dropped on its heading.
    /// </summary>
    public async Task MoveLineAsync(BudgetLine moving, BudgetLine target, bool after)
    {
        if (moving.ItemId is not { } itemId || ReferenceEquals(moving, target)) return;

        Guid? groupId = target.GroupId;
        Guid? parentId = null;
        Guid? before = null;

        if (target.IsHeader)
        {
            // Dropped on a category's heading: it becomes that category's first item.
            before = _outflowLines.FirstOrDefault(l => l.IsItem && l.GroupId == groupId && l.ParentItemId is null)?.ItemId;
        }
        else
        {
            parentId = target.ParentItemId;
            var siblings = _outflowLines.Where(l => l.IsItem && l.GroupId == groupId && l.ParentItemId == parentId).ToList();
            var index = siblings.IndexOf(target);
            before = after ? (index + 1 < siblings.Count ? siblings[index + 1].ItemId : null) : target.ItemId;
        }
        if (groupId is not { } group || before == itemId) return;

        await _gate.WaitAsync();
        string? error;
        try { error = await _store.MoveItemToAsync(itemId, group, parentId, before); }
        catch (Exception ex) { error = $"Could not move the item: {ex.Message}"; }
        finally { _gate.Release(); }

        Message = error ?? "";
        await LoadAsync();
    }

    private Guid? PreviousMonthId(Guid monthId)
    {
        var index = _months.ToList().FindIndex(m => m.MonthId == monthId);
        return index > 0 ? _months[index - 1].MonthId : null;
    }

    /// <summary>Copies the previous month's budgeted amounts into items that have none in this month yet.</summary>
    public async Task CopyLastMonthAsync(MonthColumn column)
    {
        if (PreviousMonthId(column.MonthId) is not { } previous) return;
        await _gate.WaitAsync();
        try
        {
            await _store.CopyBudgetAsync(previous, column.MonthId);
            Message = "";
            await RecalculateAsync();
        }
        catch (Exception ex) { Message = $"Could not copy: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>Sets each expense item's budget to what was spent on it in the previous month.</summary>
    public async Task BudgetLastMonthsSpendingAsync(MonthColumn column)
    {
        if (PreviousMonthId(column.MonthId) is not { } previous) return;
        await _gate.WaitAsync();
        try
        {
            var amounts = _months.First(m => m.MonthId == previous).Items.Where(i => i.Activity < 0).Select(i => (i.ItemId, -i.Activity));
            await _store.SetAllocationsAsync(column.MonthId, amounts);
            Message = "";
            await RecalculateAsync();
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }
}
