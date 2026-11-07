namespace SpeedBudget.App.ViewModels;

/// <summary>The columns of the recorded transactions table that can be filtered.</summary>
public enum FilterColumn { Date, BankDate, Counterparty, Memo, Item, Account, Outflow, Inflow, Cleared }

/// <summary>
/// The filters on the transactions table, one per column, the way Excel keeps them: a column either shows everything or only the rows
/// whose value in that column is one of the ticked values. Filters on different columns all have to match.
/// </summary>
public sealed class ColumnFilters
{
    private readonly Dictionary<FilterColumn, HashSet<string>> _allowed = new();

    public event Action? Changed;

    public bool Any => _allowed.Count > 0;

    public bool IsActive(FilterColumn column) => _allowed.ContainsKey(column);

    /// <summary>The values the column is limited to, or null when it is not filtered.</summary>
    public IReadOnlySet<string>? Allowed(FilterColumn column) => _allowed.GetValueOrDefault(column);

    /// <summary>Limits the column to these values; null removes the filter from it.</summary>
    public void Set(FilterColumn column, IEnumerable<string>? values)
    {
        if (values is null) _allowed.Remove(column);
        else _allowed[column] = new HashSet<string>(values);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_allowed.Count == 0) return;
        _allowed.Clear();
        Changed?.Invoke();
    }

    public bool Passes(TransactionRow row)
    {
        foreach (var (column, allowed) in _allowed)
            if (!allowed.Contains(row.FilterValue(column))) return false;
        return true;
    }
}
