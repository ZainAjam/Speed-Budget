using System.Globalization;

namespace SpeedBudget.Core;

/// <summary>Where a transaction or month line sits in time: its date, then its order within that date.</summary>
public readonly record struct SortKey(DateOnly Date, int DayOrder) : IComparable<SortKey>
{
    public int CompareTo(SortKey other)
    {
        var c = Date.CompareTo(other.Date);
        return c != 0 ? c : DayOrder.CompareTo(other.DayOrder);
    }

    public static bool operator <(SortKey a, SortKey b) => a.CompareTo(b) < 0;
    public static bool operator >(SortKey a, SortKey b) => a.CompareTo(b) > 0;
    public static bool operator <=(SortKey a, SortKey b) => a.CompareTo(b) <= 0;
    public static bool operator >=(SortKey a, SortKey b) => a.CompareTo(b) >= 0;
}

/// <summary>Works out where a dragged month line lands, and whether that spot is allowed.</summary>
public static class MarkerPlacement
{
    /// <summary>
    /// The key for a line dropped between the last transaction before it and the first transaction after it.
    /// A transaction level with the line counts as after it, so taking the key of the transaction below puts that
    /// transaction (and any tied with it) in the new month. Dropped at the very end, the line goes just after
    /// the last transaction. Null when there are no transactions to place it against.
    /// </summary>
    public static SortKey? Place(SortKey? lastBefore, SortKey? firstAfter)
    {
        if (firstAfter is { } after) return after;
        if (lastBefore is { } before) return new SortKey(before.Date, before.DayOrder + 1);
        return null;
    }

    /// <summary>A line may not be dragged past its neighbouring lines, nor onto the same spot as one.</summary>
    public static bool FitsBetweenOtherLines(SortKey candidate, SortKey current, IEnumerable<SortKey> otherLines)
    {
        var others = otherLines.ToList();
        var previous = others.Where(o => o <= current).Select(o => (SortKey?)o).DefaultIfEmpty(null).Max();
        var next = others.Where(o => o > current).Select(o => (SortKey?)o).DefaultIfEmpty(null).Min();
        return (previous is null || candidate > previous) && (next is null || candidate < next);
    }
}

public static class MonthLabels
{
    private const string Format = "MMMM yyyy";

    /// <summary>The label for the month a date falls in, such as "March 2024".</summary>
    public static string For(DateOnly date, CultureInfo? culture = null) =>
        date.ToString(Format, culture ?? CultureInfo.CurrentCulture);

    /// <summary>The month after a label like "January 2027", or the fallback date's month if the label isn't in that form.</summary>
    public static string Next(string? previousLabel, DateOnly fallback, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (previousLabel is not null
            && DateTime.TryParseExact(previousLabel.Trim(), Format, culture, DateTimeStyles.None, out var parsed))
            return parsed.AddMonths(1).ToString(Format, culture);
        return fallback.ToString(Format, culture);
    }
}
