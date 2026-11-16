using System.Globalization;

namespace SpeedBudget.Core;

/// <summary>One balance the user typed in for a tracked account: the balance on a date, what was withdrawn that day, and a comment.</summary>
/// <param name="Withdrawal">What was taken out of the fund that day, as a positive amount in cents.</param>
public sealed record BalanceInput(Guid Id, DateOnly Date, long Balance, long Withdrawal, string? Comment);

/// <summary>One line of the tracker, with everything worked out from the balances.</summary>
/// <param name="TotalChange">The balance now against the very first balance.</param>
/// <param name="DailyChange">The balance against the one before it (0 for the first line).</param>
/// <param name="Withdrawal">What was withdrawn, as a positive amount.</param>
/// <param name="GainLoss">What the market did that day: the change in the balance with the withdrawal put back.</param>
public sealed record BalanceRow(Guid Id, DateOnly Date, long Balance, long TotalChange, long DailyChange, long Withdrawal, long GainLoss, string? Comment, string Month);

/// <summary>The totals over every line.</summary>
public sealed record BalanceTotals(long DailyChanges, long Withdrawals, long GainsLosses);

/// <summary>One month: the market gains or losses, what was withdrawn (as a negative amount), and what is left over to reinvest (or taken from the original investment).</summary>
public sealed record MonthGains(string Month, long GainsLosses, long Withdrawals, long LeftToReinvest);

public sealed record BalanceReport(IReadOnlyList<BalanceRow> Rows, BalanceTotals Totals, IReadOnlyList<MonthGains> Months);

/// <summary>
/// Works out the daily changes, the changes to date and the gains and losses of an account tracked by its balances, as the
/// "Detailed" spreadsheet does: change = balance minus the previous balance, and gain or loss = change plus what was withdrawn.
/// </summary>
public static class BalanceTracker
{
    public static BalanceReport Calculate(IEnumerable<BalanceInput> entries)
    {
        var sorted = entries.OrderBy(e => e.Date).ThenBy(e => e.Id).ToList();
        var rows = new List<BalanceRow>(sorted.Count);
        long? first = null, previous = null;
        foreach (var e in sorted)
        {
            first ??= e.Balance;
            var daily = previous is { } p ? e.Balance - p : 0;
            rows.Add(new BalanceRow(e.Id, e.Date, e.Balance, e.Balance - first.Value, daily, e.Withdrawal, daily + e.Withdrawal, e.Comment, MonthOf(e.Date)));
            previous = e.Balance;
        }

        var totals = new BalanceTotals(rows.Sum(r => r.DailyChange), rows.Sum(r => r.Withdrawal), rows.Sum(r => r.GainLoss));
        var months = rows.GroupBy(r => r.Month).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var gains = g.Sum(r => r.GainLoss);
                var withdrawn = g.Sum(r => r.Withdrawal);
                return new MonthGains(g.Key, gains, -withdrawn, gains - withdrawn);
            }).ToList();
        return new BalanceReport(rows, totals, months);
    }

    /// <summary>The month a date is in, written 2024-02, so months sort in order.</summary>
    public static string MonthOf(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
