using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SpeedBudget.Core;
using SpeedBudget.Data;

namespace SpeedBudget.App.ViewModels;

internal static class Format
{
    public static string Money(long cents) => AppServices.ToRands(cents).ToString("N2");

    public static Brush Theme(string key) => (Brush)Application.Current.Resources[key];
}

/// <summary>An expense item in a month: budgeted amount (editable), spent and what is left.</summary>
public sealed partial class ExpenseRow(Guid itemId, string name, int depth, bool isArchived, BudgetViewModel owner) : ObservableObject
{
    private long _budgeted;

    public Guid ItemId { get; } = itemId;
    public string Name { get; } = name;
    public Thickness Indent { get; } = new(depth * 24, 0, 0, 0);
    public bool IsArchived { get; } = isArchived;

    public double Budgeted
    {
        get => (double)AppServices.ToRands(_budgeted);
        set
        {
            var cents = AppServices.ToCents(value);
            if (cents == _budgeted) return;
            _budgeted = cents;
            OnPropertyChanged();
            _ = owner.AllocationEditedAsync(ItemId, cents);
        }
    }

    [ObservableProperty] public partial string SpentText { get; set; } = "";
    [ObservableProperty] public partial string RemainingText { get; set; } = "";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial bool IsOverspent { get; set; }
    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
    [ObservableProperty] public partial Brush RemainingBrush { get; set; } = Format.Theme("TextFillColorPrimaryBrush");

    public void Apply(ItemMonth m)
    {
        _budgeted = m.Budgeted;
        OnPropertyChanged(nameof(Budgeted));
        SpentText = Format.Money(-m.Activity);
        RemainingText = Format.Money(m.Remaining);
        IsOverspent = m.Remaining < 0;
        Progress = m.Budgeted > 0 ? Math.Clamp(-m.Activity * 100.0 / m.Budgeted, 0, 100) : (m.Activity < 0 ? 100 : 0);
        RemainingBrush = Format.Theme(IsOverspent ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush");
        // An archived item only shows in months where it still has numbers.
        IsVisible = !IsArchived || m.Budgeted != 0 || m.Activity != 0;
    }
}

public sealed partial class ExpenseSection(string name) : ObservableObject
{
    public string Name { get; } = name;
    public ObservableCollection<ExpenseRow> Rows { get; } = new();

    [ObservableProperty] public partial string BudgetedText { get; set; } = "";
    [ObservableProperty] public partial string SpentText { get; set; } = "";
    [ObservableProperty] public partial string RemainingText { get; set; } = "";

    public void Apply(IReadOnlyDictionary<Guid, ItemMonth> items)
    {
        var mine = Rows.Select(r => items.GetValueOrDefault(r.ItemId)).Where(m => m is not null).ToList();
        BudgetedText = Format.Money(mine.Sum(m => m!.Budgeted));
        SpentText = Format.Money(-mine.Sum(m => m!.Activity));
        RemainingText = Format.Money(mine.Sum(m => m!.Remaining));
    }
}

/// <summary>An income item in a month: expected and held amounts are editable, received comes from transactions.</summary>
public sealed partial class IncomeRow(Guid itemId, string name, bool isArchived, BudgetViewModel owner) : ObservableObject
{
    private long _expected;
    private long _held;

    public Guid ItemId { get; } = itemId;
    public string Name { get; } = name;
    public bool IsArchived { get; } = isArchived;
    public long ReceivedCents { get; private set; }

    public double Expected
    {
        get => (double)AppServices.ToRands(_expected);
        set
        {
            var cents = AppServices.ToCents(value);
            if (cents == _expected) return;
            _expected = cents;
            OnPropertyChanged();
            _ = owner.AllocationEditedAsync(ItemId, cents);
        }
    }

    public double Held
    {
        get => (double)AppServices.ToRands(_held);
        set
        {
            var cents = AppServices.ToCents(value);
            if (cents == _held) return;
            _ = owner.HoldEditedAsync(this, cents);
        }
    }

    [ObservableProperty] public partial string ReceivedText { get; set; } = "";
    [ObservableProperty] public partial string AvailableThisMonthText { get; set; } = "";
    [ObservableProperty] public partial bool IsVisible { get; set; } = true;

    public void Apply(IncomeItemMonth m)
    {
        _expected = m.Expected;
        _held = m.Held;
        ReceivedCents = m.Received;
        OnPropertyChanged(nameof(Expected));
        OnPropertyChanged(nameof(Held));
        ReceivedText = Format.Money(m.Received);
        AvailableThisMonthText = Format.Money(m.Received - m.Held);
        IsVisible = !IsArchived || m.Expected != 0 || m.Received != 0 || m.Held != 0;
    }
}

public sealed class IncomeSection(string name)
{
    public string Name { get; } = name;
    public ObservableCollection<IncomeRow> Rows { get; } = new();
}

public sealed partial class BudgetViewModel : ObservableObject
{
    private readonly BudgetStore _store = AppServices.Store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<MonthSummary> _months = Array.Empty<MonthSummary>();
    private readonly Dictionary<Guid, ExpenseRow> _expenseRows = new();
    private readonly Dictionary<Guid, IncomeRow> _incomeRows = new();

    public ObservableCollection<IncomeSection> IncomeSections { get; } = new();
    public ObservableCollection<ExpenseSection> ExpenseSections { get; } = new();

    [ObservableProperty] public partial int MonthIndex { get; set; }
    [ObservableProperty] public partial string MonthLabel { get; set; } = "";
    [ObservableProperty] public partial bool CanGoBack { get; set; }
    [ObservableProperty] public partial bool CanGoForward { get; set; }
    [ObservableProperty] public partial bool HasPreviousMonth { get; set; }
    [ObservableProperty] public partial bool HasItems { get; set; }

    [ObservableProperty] public partial string IncomeText { get; set; } = "";
    [ObservableProperty] public partial string HeldOutText { get; set; } = "";
    [ObservableProperty] public partial string HeldInText { get; set; } = "";
    [ObservableProperty] public partial string OverflowText { get; set; } = "";
    [ObservableProperty] public partial string CarriedText { get; set; } = "";
    [ObservableProperty] public partial string BudgetedText { get; set; } = "";
    [ObservableProperty] public partial string AvailableText { get; set; } = "";
    [ObservableProperty] public partial string AvailableStatus { get; set; } = "";
    [ObservableProperty] public partial Brush AvailableBrush { get; set; } = Format.Theme("TextFillColorPrimaryBrush");

    [ObservableProperty] public partial string Message { get; set; } = "";

    public bool HasMessage => Message != "";

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    private MonthSummary? Current => MonthIndex >= 0 && MonthIndex < _months.Count ? _months[MonthIndex] : null;

    public async Task LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var groups = await _store.GetGroupsAsync();
            var items = await _store.GetItemsAsync();

            IncomeSections.Clear();
            ExpenseSections.Clear();
            _expenseRows.Clear();
            _incomeRows.Clear();

            foreach (var group in groups)
            {
                var inGroup = items.Where(i => i.GroupId == group.Id).ToList();
                if (group.Kind == ItemKind.Income)
                {
                    var section = new IncomeSection(group.Name);
                    foreach (var (item, _) in Flatten(inGroup))
                    {
                        var row = new IncomeRow(item.Id, item.Name, item.IsArchived, this);
                        section.Rows.Add(row);
                        _incomeRows[item.Id] = row;
                    }
                    IncomeSections.Add(section);
                }
                else
                {
                    var section = new ExpenseSection(group.Name);
                    foreach (var (item, depth) in Flatten(inGroup))
                    {
                        var row = new ExpenseRow(item.Id, item.Name, depth, item.IsArchived, this);
                        section.Rows.Add(row);
                        _expenseRows[item.Id] = row;
                    }
                    ExpenseSections.Add(section);
                }
            }

            HasItems = items.Count > 0;
            _months = BudgetEngine.Calculate(await _store.LoadSnapshotAsync());
            MonthIndex = _months.Count - 1;
            ApplyMonth();
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

    public void GoBack() { if (MonthIndex > 0) { MonthIndex--; ApplyMonth(); } }

    public void GoForward() { if (MonthIndex < _months.Count - 1) { MonthIndex++; ApplyMonth(); } }

    /// <summary>Pushes the current month's numbers into the existing rows, so edits never disturb focus.</summary>
    private void ApplyMonth()
    {
        if (Current is not { } m) return;

        MonthLabel = m.Label;
        CanGoBack = MonthIndex > 0;
        CanGoForward = MonthIndex < _months.Count - 1;
        HasPreviousMonth = MonthIndex > 0;

        IncomeText = Format.Money(m.Income);
        HeldOutText = Format.Money(-m.HeldOut);
        HeldInText = Format.Money(m.HeldIn);
        OverflowText = Format.Money(m.Overflow);
        CarriedText = Format.Money(m.CarriedOver);
        BudgetedText = Format.Money(-m.Budgeted);
        AvailableText = Format.Money(m.Available);
        AvailableStatus = m.Available == 0 ? "Fully budgeted" : m.Available > 0 ? "Left to budget" : "Overbudgeted";
        AvailableBrush = Format.Theme(m.Available == 0 ? "SystemFillColorSuccessBrush"
            : m.Available > 0 ? "SystemFillColorCautionBrush" : "SystemFillColorCriticalBrush");

        var items = m.Items.ToDictionary(i => i.ItemId);
        foreach (var (id, row) in _expenseRows) row.Apply(items.GetValueOrDefault(id) ?? new ItemMonth(id, 0, 0));
        foreach (var section in ExpenseSections) section.Apply(items);

        var income = m.IncomeItems.ToDictionary(i => i.ItemId);
        foreach (var (id, row) in _incomeRows) row.Apply(income.GetValueOrDefault(id) ?? new IncomeItemMonth(id, 0, 0, 0));
    }

    private async Task RecalculateAsync()
    {
        _months = BudgetEngine.Calculate(await _store.LoadSnapshotAsync());
        MonthIndex = Math.Min(MonthIndex, _months.Count - 1);
        ApplyMonth();
    }

    /// <summary>Saves a budgeted (expense) or expected (income) amount typed into the current month.</summary>
    public async Task AllocationEditedAsync(Guid itemId, long cents)
    {
        if (Current is not { } month) return;
        await _gate.WaitAsync();
        try
        {
            await _store.SetAllocationAsync(month.MonthId, itemId, cents);
            Message = "";
            await RecalculateAsync();
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>Holds part of this month's income for next month. It can't be negative or more than was received.</summary>
    public async Task HoldEditedAsync(IncomeRow row, long cents)
    {
        if (Current is not { } month) return;
        await _gate.WaitAsync();
        try
        {
            var limit = Math.Max(0, row.ReceivedCents);
            var clamped = Math.Clamp(cents, 0, limit);
            await _store.SetIncomeHoldAsync(month.MonthId, row.ItemId, clamped);
            Message = clamped == cents ? "" : cents < 0
                ? "A hold can't be negative."
                : $"Only {Format.Money(limit)} has been received for {row.Name}, so that is the most that can be held.";
            await RecalculateAsync();
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    public Task HoldAllAsync(IncomeRow row) => HoldEditedAsync(row, row.ReceivedCents);

    /// <summary>Copies last month's budgeted amounts into items that have none here yet.</summary>
    public async Task CopyLastMonthAsync()
    {
        if (Current is not { } month || MonthIndex == 0) return;
        await _gate.WaitAsync();
        try
        {
            await _store.CopyBudgetAsync(_months[MonthIndex - 1].MonthId, month.MonthId);
            Message = "";
            await RecalculateAsync();
        }
        catch (Exception ex) { Message = $"Could not copy: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    /// <summary>Sets each expense item's budget to what was spent on it last month.</summary>
    public async Task BudgetLastMonthsSpendingAsync()
    {
        if (Current is not { } month || MonthIndex == 0) return;
        await _gate.WaitAsync();
        try
        {
            var amounts = _months[MonthIndex - 1].Items.Where(i => i.Activity < 0).Select(i => (i.ItemId, -i.Activity));
            await _store.SetAllocationsAsync(month.MonthId, amounts);
            Message = "";
            await RecalculateAsync();
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }
}
