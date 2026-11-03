using System.Globalization;
using SpeedBudget.Core;

namespace SpeedBudget.Core.Tests;

public class MarkerPlacementTests
{
    private static SortKey K(int month, int day, int order = 0) => new(new DateOnly(2027, month, day), order);

    [Fact]
    public void Line_dropped_between_two_rows_takes_the_key_of_the_row_after_it()
    {
        var key = MarkerPlacement.Place(K(1, 20), K(1, 24, 3));

        Assert.Equal(K(1, 24, 3), key);
    }

    [Fact]
    public void Line_dropped_at_the_end_goes_just_after_the_last_row()
    {
        Assert.Equal(K(1, 28, 5), MarkerPlacement.Place(K(1, 28, 4), null));
    }

    [Fact]
    public void Line_dropped_above_everything_puts_every_row_in_the_new_month()
    {
        Assert.Equal(K(1, 2), MarkerPlacement.Place(null, K(1, 2)));
    }

    [Fact]
    public void With_no_rows_there_is_nothing_to_place_against()
    {
        Assert.Null(MarkerPlacement.Place(null, null));
    }

    [Fact]
    public void Placed_line_puts_the_row_after_it_in_the_new_month_and_the_row_before_it_in_the_old_one()
    {
        var before = K(1, 20);
        var after = K(1, 24);
        var marker = new MonthMarker(Guid.NewGuid(), MarkerPlacement.Place(before, after)!.Value.Date,
            MarkerPlacement.Place(before, after)!.Value.DayOrder, "Feb");
        var markers = new[] { marker };

        Assert.Equal(BudgetEngine.FirstMonthId, BudgetEngine.MonthOf(markers, before.Date, before.DayOrder));
        Assert.Equal(marker.Id, BudgetEngine.MonthOf(markers, after.Date, after.DayOrder));
    }

    [Fact]
    public void Line_cannot_be_dragged_past_or_onto_another_line()
    {
        var current = K(2, 1);
        var others = new[] { K(1, 1), K(3, 1) };

        Assert.True(MarkerPlacement.FitsBetweenOtherLines(K(2, 15), current, others));
        Assert.False(MarkerPlacement.FitsBetweenOtherLines(K(3, 5), current, others));
        Assert.False(MarkerPlacement.FitsBetweenOtherLines(K(3, 1), current, others));
        Assert.False(MarkerPlacement.FitsBetweenOtherLines(K(1, 1), current, others));
        Assert.False(MarkerPlacement.FitsBetweenOtherLines(K(12, 31), current, others));
    }

    [Fact]
    public void A_single_line_can_go_anywhere()
    {
        Assert.True(MarkerPlacement.FitsBetweenOtherLines(K(6, 1), K(2, 1), Array.Empty<SortKey>()));
    }

    [Theory]
    [InlineData("January 2027", "February 2027")]
    [InlineData("December 2027", "January 2028")]
    [InlineData("Payday run", "March 2027")]
    public void Next_label_follows_the_previous_month_or_falls_back_to_the_date(string previous, string expected)
    {
        Assert.Equal(expected, MonthLabels.Next(previous, new DateOnly(2027, 3, 14), CultureInfo.InvariantCulture));
    }
}
