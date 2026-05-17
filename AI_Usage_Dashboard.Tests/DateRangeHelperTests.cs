using AI_Usage_Dashboard.Utils;

namespace AI_Usage_Dashboard.Tests;

public class DateRangeHelperTests
{
    // ── ResolvePeriod ─────────────────────────────────────────────────────────

    [Fact]
    public void ResolvePeriod_7d_SpanIsEightDays()
    {
        var (from, to) = DateRangeHelper.ResolvePeriod("7d", null, null);
        Assert.Equal(TimeSpan.FromDays(8), to - from);
    }

    [Fact]
    public void ResolvePeriod_7d_EndsAtTomorrow()
    {
        var tomorrow = DateTime.UtcNow.Date.AddDays(1);
        var (_, to) = DateRangeHelper.ResolvePeriod("7d", null, null);
        Assert.Equal(tomorrow, to);
    }

    [Fact]
    public void ResolvePeriod_30d_SpanIsThirtyOneDays()
    {
        var (from, to) = DateRangeHelper.ResolvePeriod("30d", null, null);
        Assert.Equal(TimeSpan.FromDays(31), to - from);
    }

    [Fact]
    public void ResolvePeriod_Mtd_StartsFirstOfCurrentMonth()
    {
        var firstOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var (from, _) = DateRangeHelper.ResolvePeriod("MTD", null, null);
        Assert.Equal(firstOfMonth, from);
    }

    [Fact]
    public void ResolvePeriod_Mtd_EndsAtTomorrow()
    {
        var tomorrow = DateTime.UtcNow.Date.AddDays(1);
        var (_, to) = DateRangeHelper.ResolvePeriod("MTD", null, null);
        Assert.Equal(tomorrow, to);
    }

    [Fact]
    public void ResolvePeriod_Custom_ReturnsExactParsedDates()
    {
        var (from, to) = DateRangeHelper.ResolvePeriod("custom", "2026-01-01", "2026-01-31");
        Assert.Equal(new DateTime(2026, 1, 1), from);
        Assert.Equal(new DateTime(2026, 2, 1), to); // endDate is inclusive → +1
    }

    [Fact]
    public void ResolvePeriod_Custom_EndDateIsInclusive_AddOneDay()
    {
        var (_, to) = DateRangeHelper.ResolvePeriod("custom", "2026-04-01", "2026-04-30");
        Assert.Equal(new DateTime(2026, 5, 1), to);
    }

    [Fact]
    public void ResolvePeriod_Custom_SingleDayRange_SpanIsOneDay()
    {
        var (from, to) = DateRangeHelper.ResolvePeriod("custom", "2026-06-15", "2026-06-15");
        Assert.Equal(TimeSpan.FromDays(1), to - from);
    }

    [Fact]
    public void ResolvePeriod_Custom_InvalidStartDate_FallsBackToMtd()
    {
        var firstOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var (from, _) = DateRangeHelper.ResolvePeriod("custom", "not-a-date", "2026-04-30");
        Assert.Equal(firstOfMonth, from);
    }

    [Fact]
    public void ResolvePeriod_Custom_InvalidEndDate_FallsBackToMtd()
    {
        var firstOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var (from, _) = DateRangeHelper.ResolvePeriod("custom", "2026-01-01", "bad-date");
        Assert.Equal(firstOfMonth, from);
    }

    [Fact]
    public void ResolvePeriod_UnknownPeriod_FallsBackToMtd()
    {
        var firstOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var (from, _) = DateRangeHelper.ResolvePeriod("quarterly", null, null);
        Assert.Equal(firstOfMonth, from);
    }

    [Fact]
    public void ResolvePeriod_EmptyPeriod_FallsBackToMtd()
    {
        var firstOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var (from, _) = DateRangeHelper.ResolvePeriod("", null, null);
        Assert.Equal(firstOfMonth, from);
    }

    // ── Boundary: period strings are treated case-insensitively ──────────────

    [Theory]
    [InlineData("7D")]
    [InlineData("7d")]
    public void ResolvePeriod_7d_CaseInsensitive(string period)
    {
        var (from, to) = DateRangeHelper.ResolvePeriod(period, null, null);
        Assert.Equal(TimeSpan.FromDays(8), to - from);
    }

    [Theory]
    [InlineData("30D")]
    [InlineData("30d")]
    public void ResolvePeriod_30d_CaseInsensitive(string period)
    {
        var (from, to) = DateRangeHelper.ResolvePeriod(period, null, null);
        Assert.Equal(TimeSpan.FromDays(31), to - from);
    }

    // ── PreviousPeriod ────────────────────────────────────────────────────────

    [Fact]
    public void PreviousPeriod_SpanIsMirroredBackward()
    {
        var from = new DateTime(2026, 4, 1);
        var to   = new DateTime(2026, 4, 30);
        var (prevFrom, prevTo) = DateRangeHelper.PreviousPeriod(from, to);

        Assert.Equal(to - from, prevTo - prevFrom);   // same span length
        Assert.Equal(from - (to - from), prevFrom);    // shifted backward by span
        Assert.Equal(from, prevTo);                    // previous period ends where current starts
    }

    [Fact]
    public void PreviousPeriod_SingleDay_IsTheDayBefore()
    {
        var day = new DateTime(2026, 4, 15);
        var (prevFrom, prevTo) = DateRangeHelper.PreviousPeriod(day, day.AddDays(1));
        Assert.Equal(new DateTime(2026, 4, 14), prevFrom);
        Assert.Equal(day, prevTo);
    }

    [Fact]
    public void PreviousPeriod_LargeSpan_90Days()
    {
        var from = new DateTime(2026, 1, 1);
        var to   = new DateTime(2026, 4, 1); // 90 days
        var (prevFrom, prevTo) = DateRangeHelper.PreviousPeriod(from, to);
        Assert.Equal(TimeSpan.FromDays(90), prevTo - prevFrom);
        Assert.Equal(new DateTime(2025, 10, 3), prevFrom);
        Assert.Equal(from, prevTo);
    }

    [Fact]
    public void PreviousPeriod_PrevToEqualsCurrFrom()
    {
        var from = new DateTime(2026, 4, 1);
        var to   = new DateTime(2026, 5, 1);
        var (_, prevTo) = DateRangeHelper.PreviousPeriod(from, to);
        Assert.Equal(from, prevTo);
    }
}
