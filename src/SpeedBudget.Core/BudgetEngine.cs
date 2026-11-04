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
    /// The name of the first month. A budget has a start date (<see cref="BudgetSnapshot.BudgetStart"/>) and says which month
    /// it starts (<see cref="BudgetSnapshot.BudgetStartMonth"/>, the month the date falls in unless the user chose another: a budget that
    /// starts on 24 October can be the start of November). Budgets that have no start date yet (older ones, before it is saved) work it out as below.
    /// With no budget transactions and no month lines yet, it is the current month. Once there
    /// are transactions, it is the month the user said the first one belongs to (the stored label), or else the month of the
    /// earliest one. A month line dated before that pulls it back to the month before the line. It is always worked out from
    /// the data, so a leftover setting can never hold it in place. A first month the user renamed by hand keeps its name.
    /// </summary>
    public static string FirstMonthLabelFor(BudgetSnapshot s, DateOnly? today = null, System.Globalization.CultureInfo? culture = null)
    {
        if (s.BudgetStart is { } start) return MonthLabels.For(s.BudgetStartMonth ?? start, culture);
        if (s.FirstMonthRenamed && !string.IsNullOrWhiteSpace(s.FirstMonthLabel)) return s.FirstMonthLabel;

        var markers = SortMarkers(s.Markers);
        var firstMarker = markers.Count > 0 ? markers[0] : null;
        var onBudget = s.Accounts.Where(a => a.IsOnBudget).Select(a => a.Id).ToHashSet();
        var budgetTransactions = s.Transactions.Where(t => onBudget.Contains(t.AccountId)).ToList();

        var candidates = new List<DateOnly>();
        if (budgetTransactions.Count > 0)
        {
            if (MonthLabels.TryParse(s.FirstMonthLabel, out var chosen, culture)) candidates.Add(chosen);
            else
            {
                var inFirstMonth = budgetTransactions.Where(t => firstMarker is null
                    || Compare(firstMarker.Date, firstMarker.DayOrder, t.Date, t.DayOrder) > 0).ToList();
                if (inFirstMonth.Count > 0)
                {
                    var earliest = inFirstMonth.Min(t => t.Date);
                    candidates.Add(new DateOnly(earliest.Year, earliest.Month, 1));
                }
            }
        }

        if (firstMarker is not null)
        {
            var started = MonthLabels.TryParse(firstMarker.Label, out var named, culture)
                ? named : new DateOnly(firstMarker.Date.Year, firstMarker.Date.Month, 1);
            candidates.Add(started.AddMonths(-1));
        }

        if (candidates.Count > 0) return MonthLabels.For(candidates.Min(), culture);
        return MonthLabels.For(today ?? DateOnly.FromDateTime(DateTime.Today), culture);
    }

    /// <summary>How many months after the latest one are offered for budgeting ahead.</summary>
    public const int PlannedMonthCount = 3;

    /// <summary>
    /// Id of a month that has no month line (yet): one the user skipped between two lines, or one still to come. It is
    /// worked out from the calendar month, so the same month has the same id on every device and its budget can be filled
    /// in before any line exists. When a line is started for that month, the budget moves over to the line's id (see the store).
    /// </summary>
    public static Guid VirtualMonthId(DateOnly month) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"calendar-month:{month.Year:D4}-{month.Month:D2}")));

    /// <summary>The id the first versions used for the nth planned month after the latest line. Only needed to move old budgets over.</summary>
    public static Guid LegacyPlannedMonthId(int ordinal) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"planned-month:{ordinal}")));

    /// <summary>
    /// Calculates every month, oldest first. Only budget accounts affect the result. Every calendar month between the
    /// first month and the last month line is included, even ones the user forgot to start with a line. Optionally adds that
    /// many planned months after the later of the last line and the current month, so budgets can be set before any transaction exists.
    /// </summary>
    public static IReadOnlyList<MonthSummary> Calculate(BudgetSnapshot s, int plannedMonths = 0, DateOnly? today = null)
    {
        var markers = SortMarkers(s.Markers);
        var real = new List<(Guid Id, string Label)> { (FirstMonthId, FirstMonthLabelFor(s, today)) };
        real.AddRange(markers.Select(m => (m.Id, m.Label)));

        var months = new List<(Guid Id, string Label, bool Planned)>();
        for (var i = 0; i < real.Count; i++)
        {
            months.Add((real[i].Id, real[i].Label, false));
            if (i == real.Count - 1) break;

            // Months skipped between two lines still get a column, so nothing is hidden because a line was forgotten.
            if (MonthLabels.TryParse(real[i].Label, out var from) && MonthLabels.TryParse(real[i + 1].Label, out var to))
                for (var m = from.AddMonths(1); m < to; m = m.AddMonths(1))
                    months.Add((VirtualMonthId(m), MonthLabels.For(m), true));
        }

        if (plannedMonths > 0)
        {
            var now = today ?? DateOnly.FromDateTime(DateTime.Today);
            var currentMonth = new DateOnly(now.Year, now.Month, 1);
            if (MonthLabels.TryParse(months[^1].Label, out var last))
            {
                var end = (last > currentMonth ? last : currentMonth).AddMonths(plannedMonths);
                for (var m = last.AddMonths(1); m <= end; m = m.AddMonths(1))
                    months.Add((VirtualMonthId(m), MonthLabels.For(m), true));
            }
            else
            {
                var label = months[^1].Label;
                for (var k = 1; k <= plannedMonths; k++)
                {
                    label = MonthLabels.Next(label, now.AddMonths(k));
                    months.Add((VirtualMonthId(new DateOnly(now.Year, now.Month, 1).AddMonths(k)), label, true));
                }
            }
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
