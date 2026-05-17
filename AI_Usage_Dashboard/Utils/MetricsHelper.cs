namespace AI_Usage_Dashboard.Utils;

internal static class MetricsHelper
{
    internal static double Delta(decimal current, decimal previous) =>
        previous == 0 ? 0 : Math.Round((double)((current - previous) / previous) * 100, 1);

    internal static double Delta(long current, long previous) =>
        previous == 0 ? 0 : Math.Round((double)(current - previous) / previous * 100, 1);
}
