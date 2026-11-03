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
public sealed partial class BudgetLine(Guid? itemId, string name, int depth, bool isArchived, GroupState? group, bool isHeader) : ObservableObject
{
    public Guid? ItemId { get; } = itemId;
    public string Name { get; } = name;
    public Thickness Indent { get; } = new(depth * 20, 0, 0, 0);
    public bool IsArchived { get; } = isArchived;
    public GroupState? Group { get; } = group;
    public bool IsHeader { get; } = isHeader;
    public bool IsItem => !IsHeader;

    /// <summary>For a group heading: the items it totals.</summary>
    public List<Guid> GroupItemIds { get; } = new();

    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
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

    public void Apply(long budgeted, long activity)
    {
        _budgeted = budgeted;
        OnPropertyChanged(nameof(Budgeted));
        var left = budgeted + activity;
        BudgetedText = Format.Money(budgeted);
        SpentText = Format.Money(-activity);
        LeftText = Format.Money(left);
        LeftBrush = Format.Signed(left);
    }
}

public sealed partial class SummaryCell(bool isStrong) : ObservableObject
{
    public bool IsStrong { get; } = isStrong;

    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial Brush Brush { get; set; } = Format.Theme("TextFillColorPrimaryBrush");
}

/// <summary>One month shown as a column: a summary block, then cash inflow and cash outflow.</summary>
public sealed partial class MonthColumn : ObservableObject
{
    public Guid MonthId { get; }
    public string Label { get; private set; } = "";
    public bool HasPrevious { get; }
    public IReadOnlyList<SummaryCell> Summary { get; }
    public ObservableCollection<IncomeCell> Income { get; } = new();
    public ObservableCollection<OutflowCell> Outflow { get; } = new();

    public MonthColumn(Guid monthId, bool hasPrevious, IReadOnlyList<BudgetLine> incomeLines, IReadOnlyList<BudgetLine> outflowLines, BudgetViewModel owner)
    {
        MonthId = monthId;
        HasPrevious = hasPrevious;
        Summary = Enumerable.Range(0, 7).Select(i => new SummaryCell(isStrong: i == 6)).ToList();
        foreach (var line in incomeLines) Income.Add(new IncomeCell(line, monthId));
        foreach (var line in outflowLines) Outflow.Add(new OutflowCell(line, monthId, owner));
    }

    public void Apply(MonthSummary m)
    {
        Label = m.Label;
        OnPropertyChanged(nameof(Label));

        var values = new[] { m.Income, -m.HeldOut, m.HeldIn, m.Overflow, m.CarriedOver, -m.Budgeted, m.Available };
        for (var i = 0; i < values.Length; i++) Summary[i].Text = Format.Money(values[i]);
        Summary[6].Brush = Format.Theme(m.Available == 0 ? "SystemFillColorSuccessBrush"
            : m.Available > 0 ? "SystemFillColorCautionBrush" : "SystemFillColorCriticalBrush");

        var income = m.IncomeItems.ToDictionary(i => i.ItemId);
        foreach (var cell in Income) cell.Apply(income.GetValueOrDefault(cell.ItemId) ?? new IncomeItemMonth(cell.ItemId, 0, 0, 0));

        var items = m.Items.ToDictionary(i => i.ItemId);
        foreach (var cell in Outflow)
        {
            if (cell.Line.IsHeader)
            {
                var mine = cell.Line.GroupItemIds.Select(id => items.GetValueOrDefault(id)).Where(i => i is not null).ToList();
                cell.Apply(mine.Sum(i => i!.Budgeted), mine.Sum(i => i!.Activity));
            }
            else
            {
                var item = items.GetValueOrDefault(cell.Line.ItemId!.Value);
                cell.Apply(item?.Budgeted ?? 0, item?.Activity ?? 0);
            }
        }
    }
}

public sealed partial class BudgetViewModel : ObservableObject
{
    /// <summary>The labels down the left of the summary block, in the order of each month's summary cells.</summary>
    public static readonly IReadOnlyList<string> SummaryLabels = new[]
    {
        "Income this month", "Held for next month", "Released from last month", "Overflow from last month",
        "Carried over", "Budgeted", "Available to budget",
    };

    private readonly BudgetStore _store = AppServices.Store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<MonthSummary> _months = Array.Empty<MonthSummary>();
    private readonly List<BudgetLine> _incomeLines = new();
    private readonly List<BudgetLine> _outflowLines = new();
    private int _start;
    private int _capacity = 1;
    private bool _firstLoad = true;

    public ObservableCollection<BudgetLine> IncomeLines { get; } = new();
    public ObservableCollection<BudgetLine> OutflowLines { get; } = new();
    public ObservableCollection<MonthColumn> Columns { get; } = new();

    [ObservableProperty] public partial bool CanGoBack { get; set; }
    [ObservableProperty] public partial bool CanGoForward { get; set; }
    [ObservableProperty] public partial bool HasItems { get; set; }
    [ObservableProperty] public partial string RangeText { get; set; } = "";
    [ObservableProperty] public partial string Message { get; set; } = "";

    public bool HasMessage => Message != "";

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    private int MaxStart => Math.Max(0, _months.Count - _capacity);

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
                        _incomeLines.Add(new BudgetLine(item.Id, item.Name, depth, item.IsArchived, null, isHeader: false));
                    continue;
                }

                var state = new GroupState(group.Name);
                state.PropertyChanged += (_, _) => ApplyVisibility();
                var header = new BudgetLine(null, group.Name, 0, false, state, isHeader: true);
                _outflowLines.Add(header);
                foreach (var (item, depth) in Flatten(inGroup))
                {
                    _outflowLines.Add(new BudgetLine(item.Id, item.Name, depth, item.IsArchived, state, isHeader: false));
                    header.GroupItemIds.Add(item.Id);
                }
            }

            IncomeLines.Clear();
            foreach (var l in _incomeLines) IncomeLines.Add(l);
            OutflowLines.Clear();
            foreach (var l in _outflowLines) OutflowLines.Add(l);

            HasItems = items.Count > 0;
            _months = BudgetEngine.Calculate(await _store.LoadSnapshotAsync());
            if (_firstLoad) { _start = MaxStart; _firstLoad = false; }
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
        if (capacity == _capacity) return;
        _capacity = capacity;
        if (_months.Count == 0) return;

        // Keep the latest month in view when the window grows.
        _start = Math.Clamp(_start, 0, MaxStart);
        RebuildColumns();
    }

    public void GoBack()
    {
        if (_start == 0) return;
        _start--;
        RebuildColumns();
    }

    public void GoForward()
    {
        if (_start >= MaxStart) return;
        _start++;
        RebuildColumns();
    }

    private void RebuildColumns()
    {
        Columns.Clear();
        for (var i = _start; i < Math.Min(_months.Count, _start + _capacity); i++)
            Columns.Add(new MonthColumn(_months[i].MonthId, hasPrevious: i > 0, _incomeLines, _outflowLines, this));
        ApplyData();
    }

    /// <summary>Pushes the calculated numbers into the existing cells, so editing never disturbs focus.</summary>
    private void ApplyData()
    {
        foreach (var column in Columns)
            column.Apply(_months.First(m => m.MonthId == column.MonthId));

        CanGoBack = _start > 0;
        CanGoForward = _start < MaxStart;
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
            header.IsVisible = _outflowLines.Any(l => l.IsItem && l.Group == header.Group && Relevant(l));
    }

    private async Task RecalculateAsync()
    {
        _months = BudgetEngine.Calculate(await _store.LoadSnapshotAsync());
        _start = Math.Clamp(_start, 0, MaxStart);
        var wanted = _months.Skip(_start).Take(_capacity).Select(m => m.MonthId).ToList();
        if (wanted.SequenceEqual(Columns.Select(c => c.MonthId))) ApplyData();
        else RebuildColumns();
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
