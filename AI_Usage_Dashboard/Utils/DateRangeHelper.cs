namespace AI_Usage_Dashboard.Utils;

internal static class DateRangeHelper
{
    // Resolves period|startDate|endDate into a [from, to) UTC date window.
    // - period takes precedence when it's a known label (today/7d/30d/MTD)
    // - period=custom or absent + startDate/endDate present → custom window
    // - everything else → MTD
    internal static (DateTime from, DateTime to) ResolvePeriod(
        string? period,
        string? startDate,
        string? endDate)
    {
        var now = DateTime.UtcNow.Date;
        var key = (period ?? string.Empty).Trim().ToLowerInvariant();

        switch (key)
        {
            case "today":
                return (now, now.AddDays(1));
            case "7d":
                return (now.AddDays(-7), now.AddDays(1));
            case "30d":
                return (now.AddDays(-30), now.AddDays(1));
            case "mtd":
                return (new DateTime(now.Year, now.Month, 1), now.AddDays(1));
            case "custom":
            case "":
                if (DateTime.TryParse(startDate, out var s) && DateTime.TryParse(endDate, out var e))
                    return (s.Date, e.Date.AddDays(1));
                break;
        }

        // Fallback: MTD
        return (new DateTime(now.Year, now.Month, 1), now.AddDays(1));
    }

    internal static (DateTime from, DateTime to) PreviousPeriod(DateTime from, DateTime to)
    {
        var span = to - from;
        return (from - span, from);
    }
}
