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

    /// <summary>Calculates every month, oldest first. Only budget accounts affect the result.</summary>
    public static IReadOnlyList<MonthSummary> Calculate(BudgetSnapshot s)
    {
        var markers = SortMarkers(s.Markers);
        var months = new List<(Guid Id, string Label)> { (FirstMonthId, s.FirstMonthLabel) };
        months.AddRange(markers.Select(m => (m.Id, m.Label)));

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

        foreach (var (monthId, label) in months)
        {
            long income = 0, budgeted = 0, remaining = 0;
            var items = new List<ItemMonth>();

            foreach (var item in s.Items)
            {
                var act = activity.GetValueOrDefault((monthId, item.Id));
                if (item.Kind == ItemKind.Income)
                {
                    income += act;
                    continue;
                }
                var bud = s.Allocations.Where(a => a.MonthId == monthId && a.BudgetItemId == item.Id).Sum(a => a.Amount);
                var im = new ItemMonth(item.Id, bud, act);
                items.Add(im);
                budgeted += bud;
                remaining += im.Remaining;
            }

            var heldOut = s.Holds.Where(h => h.MonthId == monthId).Sum(h => h.Amount);
            var overflow = previousRemaining;
            var carried = previousAvailable;
            var available = income - heldOut + previousHeldOut + overflow + carried - budgeted;

            results.Add(new MonthSummary(monthId, label, income, heldOut, previousHeldOut, overflow, carried, budgeted, available, items));
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
