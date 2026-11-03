namespace SpeedBudget.Core;

/// <summary>Money is always integer cents. Positive = inflow, negative = outflow.</summary>
public enum ItemKind { Expense, Income }

public sealed record Account(Guid Id, string Name, bool IsOnBudget);

public sealed record BudgetItem(Guid Id, string Name, ItemKind Kind);

/// <summary>The "Let's start a new month" line. Sorts with transactions by (Date, DayOrder).</summary>
public sealed record MonthMarker(Guid Id, DateOnly Date, int DayOrder, string Label);

public sealed record SplitLine(Guid? BudgetItemId, long Amount);

public sealed record Transaction(
    Guid Id,
    Guid AccountId,
    DateOnly Date,
    int DayOrder,
    long Amount,
    IReadOnlyList<SplitLine> Splits)
{
    /// <summary>Split lines must add up to the transaction amount.</summary>
    public bool IsBalanced => Splits.Sum(s => s.Amount) == Amount;
}

/// <summary>Budgeted (expense item) or expected (income item) amount for a month.</summary>
public sealed record Allocation(Guid MonthId, Guid BudgetItemId, long Amount);

/// <summary>Part of a month's income held back so it is only available to budget next month.</summary>
public sealed record IncomeHold(Guid MonthId, Guid IncomeItemId, long Amount);

public sealed record BudgetSnapshot(
    IReadOnlyList<Account> Accounts,
    IReadOnlyList<BudgetItem> Items,
    IReadOnlyList<MonthMarker> Markers,
    IReadOnlyList<Transaction> Transactions,
    IReadOnlyList<Allocation> Allocations,
    IReadOnlyList<IncomeHold> Holds,
    string FirstMonthLabel = "");

public sealed record ItemMonth(Guid ItemId, long Budgeted, long Activity)
{
    /// <summary>What is left: budgeted plus activity (outflows are negative).</summary>
    public long Remaining => Budgeted + Activity;
}

/// <summary>An income item in a month: what you expect, what arrived, and how much of it is held for next month.</summary>
public sealed record IncomeItemMonth(Guid ItemId, long Expected, long Received, long Held);

public sealed record MonthSummary(
    Guid MonthId,
    string Label,
    long Income,
    long HeldOut,
    long HeldIn,
    long Overflow,
    long CarriedOver,
    long Budgeted,
    long Available,
    IReadOnlyList<ItemMonth> Items,
    IReadOnlyList<IncomeItemMonth> IncomeItems);
