namespace Sortify.Services;

/// <summary>The days, weeks or months the charts over time add listening up into.</summary>
public static class TimeBuckets
{
    /// <summary>The start of the bucket <paramref name="when"/> falls in: its day, its week from <paramref name="firstDay"/>, or its month.</summary>
    public static DateTime Of(DateTime when, ChartBuilder.TimeGranularity granularity, DayOfWeek firstDay = DayOfWeek.Sunday) => granularity switch
    {
        ChartBuilder.TimeGranularity.Weekly => Weeks.Start(when, firstDay),
        ChartBuilder.TimeGranularity.Daily => when.Date,
        _ => new DateTime(when.Year, when.Month, 1),
    };

    /// <summary>
    /// Every bucket from the one <paramref name="first"/> falls in to the one <paramref name="last"/>
    /// falls in, none skipped, so a chart can show a quiet one as zero rather than drawing its
    /// line straight across it.
    /// </summary>
    public static List<DateTime> Between(
        DateTime first, DateTime last, ChartBuilder.TimeGranularity granularity, DayOfWeek firstDay = DayOfWeek.Sunday)
    {
        var to = Of(last, granularity, firstDay);
        var buckets = new List<DateTime>();

        // Stops on the last bucket rather than past it: after December 9999 there is no next one.
        for (var bucket = Of(first, granularity, firstDay); ; bucket = Next(bucket, granularity))
        {
            buckets.Add(bucket);
            if (bucket >= to)
                return buckets;
        }
    }

    private static DateTime Next(DateTime bucket, ChartBuilder.TimeGranularity granularity) => granularity switch
    {
        ChartBuilder.TimeGranularity.Weekly => bucket.AddDays(7),
        ChartBuilder.TimeGranularity.Daily => bucket.AddDays(1),
        _ => bucket.AddMonths(1),
    };
}
