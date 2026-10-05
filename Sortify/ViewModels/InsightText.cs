using Sortify.Models;
using Sortify.Services;

namespace Sortify.ViewModels;

/// <summary>
/// Figures from one set of results put into words: the Insights tab's, the Podcasts tab's,
/// and the lines under the monthly leaders and the year-found chart. A figure with nothing to
/// show reads "-".
/// </summary>
public sealed class InsightText
{
    public static InsightText Empty { get; } = For(AnalysisResult.Empty);

    public string LongestStreak { get; private init; } = "-";
    public string CurrentStreak { get; private init; } = "-";
    public string LongestBreak { get; private init; } = "-";
    public string BiggestDay { get; private init; } = "-";
    public string PeakHour { get; private init; } = "-";
    public string ActiveDays { get; private init; } = "-";
    public string AveragePerDay { get; private init; } = "-";
    public string Sessions { get; private init; } = "-";
    public string AverageSession { get; private init; } = "-";
    public string LongestSession { get; private init; } = "-";
    public string WeekSplit { get; private init; } = "-";
    public string TimeOfDay { get; private init; } = "-";
    public string SkipRate { get; private init; } = "-";
    public string CompletionRate { get; private init; } = "-";
    public string RepeatRate { get; private init; } = "-";
    public string DiscoveryRate { get; private init; } = "-";
    public string TopArtistShare { get; private init; } = "-";
    public string ShuffleRate { get; private init; } = "-";
    public string OfflineRate { get; private init; } = "-";
    public string PrivateSessions { get; private init; } = "-";
    public string TopDevice { get; private init; } = "-";
    public string PodcastTime { get; private init; } = "-";
    public string PodcastPlays { get; private init; } = "-";
    public string Shows { get; private init; } = "-";
    public string Episodes { get; private init; } = "-";

    public static InsightText For(AnalysisResult r) => new()
    {
        LongestStreak = DaysFromTo(r.LongestStreakDays, r.LongestStreakStart, r.LongestStreakEnd),
        CurrentStreak = r.CurrentStreakDays > 0 && r.LastListen is { } last
            ? $"{Plural.Of(r.CurrentStreakDays, "day")}  (up to {TimeFormat.Day(last)})"
            : "-",
        LongestBreak = DaysFromTo(r.LongestBreakDays, r.LongestBreakStart, r.LongestBreakEnd),
        BiggestDay = r.BiggestDay is { } day ? $"{TimeFormat.Day(day)}  ({Duration(r.BiggestDayMs)})" : "-",
        PeakHour = BusiestHour(r),
        ActiveDays = Count(r.ActiveDays),
        AveragePerDay = r.ActiveDays > 0 ? Duration((double)r.TotalMsPlayed / r.ActiveDays) : "-",
        Sessions = Count(r.SessionCount),
        AverageSession = r.SessionCount > 0 ? Duration(r.AvgSessionMs) : "-",
        LongestSession = r.LongestSessionMs > 0 && r.LongestSessionDate is { } sessionDay
            ? $"{Duration(r.LongestSessionMs)}  ({TimeFormat.Day(sessionDay)})"
            : "-",
        WeekSplit = SplitOfTheWeek(r),
        TimeOfDay = BusiestTimeOfDay(r),
        SkipRate = r.SkipEligiblePlays > 0
            ? $"{Percent(r.TotalSkips, r.SkipEligiblePlays)}  ({r.TotalSkips:N0} of {Plural.Of(r.SkipEligiblePlays, "play")})"
            : "-",
        CompletionRate = r.SkipEligiblePlays > 0
            ? $"{Percent(r.CompletedPlays, r.SkipEligiblePlays)}  ({Plural.Of(r.CompletedPlays, "play")})"
            : "-",
        RepeatRate = r.UniqueTracks > 0 ? $"{r.PlaysPerTrack:0.0} plays per track" : "-",
        DiscoveryRate = r.NewArtistsByMonth.Count > 0 ? $"{r.NewArtistsPerMonth:0.#} new artists / month" : "-",
        TopArtistShare = r.Artists.Count > 0
            ? $"{r.Artists[0].Artist}  ({r.TopArtistSharePercent:0.#}% of your time)"
            : "-",

        // Older exports omit the playback flags, so these are shares of the plays that record them.
        ShuffleRate = r.ShuffleEligiblePlays > 0
            ? $"{Percent(r.ShufflePlays, r.ShuffleEligiblePlays)}  ({r.ShufflePlays:N0} of {Plural.Of(r.ShuffleEligiblePlays, "play")})"
            : "-",
        OfflineRate = r.ShuffleEligiblePlays > 0
            ? $"{Percent(r.OfflinePlays, r.ShuffleEligiblePlays)}  ({Plural.Of(r.OfflinePlays, "play")})"
            : "-",
        PrivateSessions = r.ShuffleEligiblePlays > 0
            ? $"{Percent(r.IncognitoPlays, r.ShuffleEligiblePlays)}  ({Plural.Of(r.IncognitoPlays, "play")})"
            : "-",
        TopDevice = r.Platforms.Count > 0
            ? $"{r.Platforms[0].Name}  ({TimeFormat.Friendly(r.Platforms[0].TotalTime)})"
            : "-",

        PodcastTime = r.PodcastMsPlayed > 0 ? Duration(r.PodcastMsPlayed) : "-",
        PodcastPlays = Count(r.PodcastPlays),
        Shows = Count(r.Shows.Count),
        Episodes = Count(r.Episodes.Count),
    };

    private static string Count(int value) => value > 0 ? value.ToString("N0") : "-";

    private static string Duration(double ms) => TimeFormat.Friendly(TimeSpan.FromMilliseconds(ms));

    private static string Percent(int part, int whole) => $"{part * 100.0 / whole:0.#}%";

    private static string DaysFromTo(int days, DateTime? from, DateTime? to) =>
        days > 0 && from is { } start && to is { } end
            ? $"{Plural.Of(days, "day")}  ({TimeFormat.Day(start)} to {TimeFormat.Day(end)})"
            : "-";

    /// <summary>The busiest hour of the week, read off the same day-by-hour grid as the heatmap.</summary>
    private static string BusiestHour(AnalysisResult r)
    {
        int peakDow = 0, peakHour = 0;
        for (int d = 0; d < 7; d++)
        {
            for (int h = 0; h < 24; h++)
            {
                if (r.PlaytimeByDowHour[d, h] > r.PlaytimeByDowHour[peakDow, peakHour])
                    (peakDow, peakHour) = (d, h);
            }
        }

        // Undated plays count toward the totals but never reach the grid.
        if (r.PlaytimeByDowHour[peakDow, peakHour] == 0)
            return "-";

        string[] dayNames = { "Sundays", "Mondays", "Tuesdays", "Wednesdays", "Thursdays", "Fridays", "Saturdays" };
        return $"{peakHour:00}:00-{(peakHour + 1) % 24:00}:00 on {dayNames[peakDow]}";
    }

    private static string SplitOfTheWeek(AnalysisResult r)
    {
        long weekend = r.PlaytimeByDayOfWeek[0] + r.PlaytimeByDayOfWeek[6];
        long total = 0;
        foreach (var ms in r.PlaytimeByDayOfWeek) total += ms;
        if (total == 0)
            return "-";

        double weekendPct = weekend * 100.0 / total;
        return $"{100 - weekendPct:0}% weekdays  /  {weekendPct:0}% weekends";
    }

    private static string BusiestTimeOfDay(AnalysisResult r)
    {
        long total = 0;
        foreach (var ms in r.PlaytimeByHour) total += ms;
        if (total == 0)
            return "-";

        Span<long> segments = stackalloc long[4]; // night, morning, afternoon, evening
        for (int h = 0; h < 24; h++)
            segments[h / 6] += r.PlaytimeByHour[h];

        string[] names = { "Night (00-06)", "Morning (06-12)", "Afternoon (12-18)", "Evening (18-24)" };
        int best = 0;
        for (int i = 1; i < 4; i++)
            if (segments[i] > segments[best]) best = i;

        return $"{names[best]}  ({segments[best] * 100.0 / total:0}% of listening)";
    }

    /// <summary>
    /// Who topped the most months, and their longest run of back-to-back months on top. Ties
    /// go to the artist with more listening in the months they led.
    /// </summary>
    internal static string MonthlyLeader(IReadOnlyList<MonthStat> months)
    {
        if (months.Count == 0)
            return string.Empty;

        var leader = months
            .GroupBy(m => m.TopArtist)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Sum(m => m.TopArtistMs))
            .First();

        int best = 0, run = 0;
        DateTime? previous = null;
        DateTime runStart = default, bestStart = default, bestEnd = default;
        foreach (var month in months.Where(m => m.TopArtist == leader.Key))
        {
            run = previous is { } p && p.AddMonths(1) == month.Month ? run + 1 : 1;
            if (run == 1)
                runStart = month.Month;
            if (run > best)
            {
                best = run;
                bestStart = runStart;
                bestEnd = month.Month;
            }
            previous = month.Month;
        }

        int count = leader.Count();
        string text = $"{leader.Key} was your #1 artist in {count} of {Plural.Of(months.Count, "month")}";
        return best > 1
            ? $"{text}, {best} of them in a row ({TimeFormat.Month(bestStart)} to {TimeFormat.Month(bestEnd)})."
            : text + ".";
    }

    /// <summary>The latest year's share of new music, and of music from the year before it.</summary>
    internal static string Cohorts(CohortBreakdown c)
    {
        if (c.Years.Count == 0)
            return string.Empty;
        if (c.Years.Count == 1)
            return $"With only {c.Years[0]} in the history, all of it counts as music first heard that year.";

        int year = c.Years[^1];
        string text = $"{c.NewSharePercent(year):0}% of {year}'s listening was music you first heard that year";
        double before = c.SharePercent(year, year - 1);
        return before > 0
            ? $"{text}, and {before:0}% was music found in {year - 1}. The first year of a history is all new by definition."
            : $"{text}. The first year of a history is all new by definition.";
    }
}
