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

    /// <summary>
    /// The name of the first month. It is never fixed: it is the earliest of the month the budget was set up to begin with,
    /// the month of the first transaction in a budget account, and the month before the first month line. So a
    /// transaction or a line dated earlier moves the first month back. A first month the user has renamed by hand
    /// keeps its name. With nothing to go on, it is the current month.
    /// </summary>
    public static string FirstMonthLabelFor(BudgetSnapshot s, DateOnly? today = null, System.Globalization.CultureInfo? culture = null)
    {
        if (s.FirstMonthRenamed && !string.IsNullOrWhiteSpace(s.FirstMonthLabel)) return s.FirstMonthLabel;

        var markers = SortMarkers(s.Markers);
        var firstMarker = markers.Count > 0 ? markers[0] : null;
        var candidates = new List<DateOnly>();

        if (MonthLabels.TryParse(s.FirstMonthLabel, out var chosen, culture)) candidates.Add(chosen);

        var onBudget = s.Accounts.Where(a => a.IsOnBudget).Select(a => a.Id).ToHashSet();
        var inFirstMonth = s.Transactions.Where(t => onBudget.Contains(t.AccountId)
            && (firstMarker is null || Compare(firstMarker.Date, firstMarker.DayOrder, t.Date, t.DayOrder) > 0)).ToList();
        if (inFirstMonth.Count > 0)
        {
            var earliest = inFirstMonth.Min(t => t.Date);
            candidates.Add(new DateOnly(earliest.Year, earliest.Month, 1));
        }

        if (firstMarker is not null)
        {
            var started = MonthLabels.TryParse(firstMarker.Label, out var named, culture)
                ? named : new DateOnly(firstMarker.Date.Year, firstMarker.Date.Month, 1);
            candidates.Add(started.AddMonths(-1));
        }

        if (candidates.Count == 0)
        {
            var now = today ?? DateOnly.FromDateTime(DateTime.Today);
            return string.IsNullOrWhiteSpace(s.FirstMonthLabel) ? MonthLabels.For(now, culture) : s.FirstMonthLabel;
        }
        return MonthLabels.For(candidates.Min(), culture);
    }

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
        var months = new List<(Guid Id, string Label, bool Planned)> { (FirstMonthId, FirstMonthLabelFor(s), false) };
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
        long previousHeldOut = 0, previousDeficit = 0, previousAvailable = 0;

        // What each item is carrying into the month: its positive balance at the end of the month before.
        var carriedIntoMonth = new Dictionary<Guid, long>();

        foreach (var (monthId, label, planned) in months)
        {
            long income = 0, budgeted = 0, deficit = 0;
            var items = new List<ItemMonth>();
            var carryOut = new Dictionary<Guid, long>();
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
                var im = new ItemMonth(item.Id, bud, act, carriedIntoMonth.GetValueOrDefault(item.Id));
                items.Add(im);
                budgeted += bud;

                // A positive balance stays with the item next month. A negative one is cleared from the item (it starts
                // the next month from zero) and instead comes off what is available to budget as overflow.
                if (im.Remaining > 0) carryOut[item.Id] = im.Remaining;
                else deficit += im.Remaining;
            }

            var heldOut = s.Holds.Where(h => h.MonthId == monthId).Sum(h => h.Amount);
            var overflow = previousDeficit;
            var carried = previousAvailable;
            var available = income - heldOut + previousHeldOut + overflow + carried - budgeted;

            results.Add(new MonthSummary(monthId, label, income, heldOut, previousHeldOut, overflow, carried, budgeted, available, items, incomeItems, planned));
            previousHeldOut = heldOut;
            previousDeficit = deficit;
            carriedIntoMonth = carryOut;
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
