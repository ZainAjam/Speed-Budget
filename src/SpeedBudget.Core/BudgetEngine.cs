namespace SpeedBudget.Core;

public static class BudgetEngine
{
    /// <summary>Id of the first month, which is not started by a marker.</summary>
    public static readonly Guid FirstMonthId = Guid.Empty;

    /// <summary>
    /// The month a transaction belongs to: the month started by the last marker at or before it
    /// in (Date, DayOrder) order. A transaction level with a marker falls after it, in the new month.
    /// </summary>
    public static Guid MonthOf(IReadOnlyList<MonthMarker> sortedMarkers, DateOnly date, int dayOrder)
    {
        var month = FirstMonthId;
        foreach (var marker in sortedMarkers)
        {
            if (Compare(marker.Date, marker.DayOrder, date, dayOrder) <= 0) month = marker.Id;
            else break;
        }
        return month;
    }

    public static IReadOnlyList<MonthMarker> SortMarkers(IEnumerable<MonthMarker> markers) =>
        markers.OrderBy(m => m.Date).ThenBy(m => m.DayOrder).ToList();

    /// <summary>How many months after the latest real one are offered for budgeting ahead.</summary>
    public const int PlannedMonthCount = 3;

    /// <summary>
    /// Id of the nth month after the latest real month (1 is the very next one). A planned month has no month line
    /// yet, but its budget can already be filled in; the same id on every device keeps that budget in sync. When a
    /// real month is started, the planned budgets move down one place (see the store).
    /// </summary>
    public static Guid PlannedMonthId(int ordinal) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"planned-month:{ordinal}")));

    /// <summary>
    /// Calculates every month, oldest first. Only budget accounts affect the result. Optionally adds that many
    /// planned months after the last real one, so budgets can be set before any transaction exists for them.
    /// </summary>
    public static IReadOnlyList<MonthSummary> Calculate(BudgetSnapshot s, int plannedMonths = 0)
    {
        var markers = SortMarkers(s.Markers);
        var months = new List<(Guid Id, string Label, bool Planned)> { (FirstMonthId, s.FirstMonthLabel, false) };
        months.AddRange(markers.Select(m => (m.Id, m.Label, false)));

        var previousLabel = months[^1].Label;
        for (var k = 1; k <= plannedMonths; k++)
        {
            previousLabel = MonthLabels.Next(previousLabel, DateOnly.FromDateTime(DateTime.Today).AddMonths(k));
            months.Add((PlannedMonthId(k), previousLabel, true));
        }

        var onBudget = s.Accounts.Where(a => a.IsOnBudget).Select(a => a.Id).ToHashSet();
        var kinds = s.Items.ToDictionary(i => i.Id, i => i.Kind);

        // Sum split lines per (month, item).
        var activity = new Dictionary<(Guid Month, Guid Item), long>();
        foreach (var t in s.Transactions)
        {
            if (!onBudget.Contains(t.AccountId)) continue;
            var month = MonthOf(markers, t.Date, t.DayOrder);
            foreach (var line in t.Splits)
            {
                if (line.BudgetItemId is not { } item || !kinds.ContainsKey(item)) continue;
                activity[(month, item)] = activity.GetValueOrDefault((month, item)) + line.Amount;
            }
        }

        var results = new List<MonthSummary>();
        long previousHeldOut = 0, previousRemaining = 0, previousAvailable = 0;

        foreach (var (monthId, label, planned) in months)
        {
            long income = 0, budgeted = 0, remaining = 0;
            var items = new List<ItemMonth>();
            var incomeItems = new List<IncomeItemMonth>();

            foreach (var item in s.Items)
            {
                var act = activity.GetValueOrDefault((monthId, item.Id));
                var bud = s.Allocations.Where(a => a.MonthId == monthId && a.BudgetItemId == item.Id).Sum(a => a.Amount);
                if (item.Kind == ItemKind.Income)
                {
                    var held = s.Holds.Where(h => h.MonthId == monthId && h.IncomeItemId == item.Id).Sum(h => h.Amount);
                    incomeItems.Add(new IncomeItemMonth(item.Id, bud, act, held));
                    income += act;
                    continue;
                }
                var im = new ItemMonth(item.Id, bud, act);
                items.Add(im);
                budgeted += bud;
                remaining += im.Remaining;
            }

            var heldOut = s.Holds.Where(h => h.MonthId == monthId).Sum(h => h.Amount);
            var overflow = previousRemaining;
            var carried = previousAvailable;
            var available = income - heldOut + previousHeldOut + overflow + carried - budgeted;

            results.Add(new MonthSummary(monthId, label, income, heldOut, previousHeldOut, overflow, carried, budgeted, available, items, incomeItems, planned));
            previousHeldOut = heldOut;
            previousRemaining = remaining;
            previousAvailable = available;
        }
        return results;
    }

    private static int Compare(DateOnly d1, int o1, DateOnly d2, int o2)
    {
        var c = d1.CompareTo(d2);
        return c != 0 ? c : o1.CompareTo(o2);
    }
}
