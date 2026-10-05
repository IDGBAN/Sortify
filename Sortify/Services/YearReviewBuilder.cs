using System.Globalization;
using Sortify.Models;

namespace Sortify.Services;

/// <summary>Works out what goes on a summary card: a calendar year's, or any other range's.</summary>
public static class YearReviewBuilder
{
    /// <summary>How many artists and tracks the card lists.</summary>
    public const int TopCount = 5;

    /// <summary>How many figures the row under the minutes holds.</summary>
    public const int StatCount = 4;

    /// <summary>Ranges at least this many days long name their biggest month. Shorter ones name a week instead.</summary>
    public const int BiggestMonthMinDays = 60;

    /// <summary>Ranges shorter than this have too few weeks for the biggest to mean much, so they only name a day.</summary>
    public const int BiggestWeekMinDays = 14;

    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    public static Task<YearReview> BuildAsync(
        IReadOnlyList<PlayRecord> records, FilterOptions filter, int year, string filterNote,
        TimeSpan? sessionGap = null, CancellationToken cancellationToken = default)
        => Task.Run(() => Build(records, filter, year, filterNote, sessionGap, cancellationToken), cancellationToken);

    /// <summary>The year under the sidebar filters, whatever date range the sidebar has.</summary>
    public static YearReview Build(
        IReadOnlyList<PlayRecord> records, FilterOptions filter, int year, string filterNote,
        TimeSpan? sessionGap = null, CancellationToken cancellationToken = default)
        => BuildForDates(records, filter, new DateTime(year, 1, 1), new DateTime(year, 12, 31), filterNote,
            sessionGap, DayOfWeek.Sunday, cancellationToken);

    public static Task<YearReview> BuildForDatesAsync(
        IReadOnlyList<PlayRecord> records, FilterOptions filter, DateTime? from, DateTime? to, string filterNote,
        TimeSpan? sessionGap = null, DayOfWeek firstDayOfWeek = DayOfWeek.Sunday, CancellationToken cancellationToken = default)
        => Task.Run(() => BuildForDates(records, filter, from, to, filterNote, sessionGap, firstDayOfWeek, cancellationToken),
            cancellationToken);

    /// <summary>
    /// The days from <paramref name="from"/> to <paramref name="to"/>, both included, under the
    /// sidebar's other filters. Either end may be open; with neither, the card covers the whole
    /// history. Whether an artist is new is judged against the whole history, not just the range.
    /// </summary>
    public static YearReview BuildForDates(
        IReadOnlyList<PlayRecord> records, FilterOptions filter, DateTime? from, DateTime? to, string filterNote,
        TimeSpan? sessionGap = null, DayOfWeek firstDayOfWeek = DayOfWeek.Sunday, CancellationToken cancellationToken = default)
    {
        var r = AnalysisEngine.Analyze(
            records, filter.WithDateRange(from?.Date, to?.Date.AddDays(1).AddTicks(-1)), sessionGap, cancellationToken);
        var range = CardRange.For(from, to, r.FirstListen, r.LastListen)
            ?? new CardRange(DateTime.Today, DateTime.Today, allTime: true);

        int newArtists = range.AllTime
            ? 0
            : FirstHeard(records, filter).Values.Count(d => d.Date >= range.From && d.Date <= range.To);

        var stats = new List<YearReviewStat> { Stat(r.TotalPlays, "play", "plays"), Stat(r.UniqueArtists, "artist", "artists") };
        if (!range.AllTime)
            stats.Add(Stat(newArtists, "new artist", "new artists"));
        if (range.Days > 1)
            stats.Add(Stat(r.LongestStreakDays, "day in a row", "days in a row"));
        stats.Add(Stat(r.UniqueTracks, "track", "tracks"));
        stats.Add(Stat(r.UniqueAlbums, "album", "albums"));

        bool oneYear = range.WithinOneYear;
        var biggestMonth = range.Days >= BiggestMonthMinDays ? r.Months.MaxBy(m => m.TotalMsPlayed) : null;
        var biggestDay = range.Days > 1 ? r.BiggestDay : null;
        return new YearReview
        {
            Range = range,
            TotalMsPlayed = r.TotalMsPlayed,
            Plays = r.TotalPlays,
            Artists = r.UniqueArtists,
            Tracks = r.UniqueTracks,
            NewArtists = newArtists,
            LongestStreakDays = r.LongestStreakDays,
            Stats = stats.Take(StatCount).ToList(),
            TopArtists = r.Artists.Take(TopCount)
                .Select((a, i) => new YearReviewEntry(i + 1, a.Artist, string.Empty, Hours(a.TotalMsPlayed)))
                .ToList(),
            TopTracks = r.Tracks.Take(TopCount)
                .Select((t, i) => new YearReviewEntry(i + 1, t.Track, t.Artist, Hours(t.TotalMsPlayed)))
                .ToList(),
            BiggestMonth = biggestMonth?.Month,
            BiggestMonthMs = biggestMonth?.TotalMsPlayed ?? 0,
            BiggestDay = biggestDay,
            BiggestDayMs = biggestDay is null ? 0 : r.BiggestDayMs,
            BiggestMonthText = biggestMonth is { } m
                ? $"{m.Month.ToString(oneYear ? "MMMM" : "MMMM yyyy", En)}, {HoursInWords(m.TotalMsPlayed)}"
                : string.Empty,
            BiggestWeekText = range.Days is >= BiggestWeekMinDays and < BiggestMonthMinDays
                ? BiggestWeek(r, range, firstDayOfWeek)
                : string.Empty,
            BiggestDayText = biggestDay is { } d
                ? $"{d.ToString(oneYear ? "MMMM d" : "MMMM d, yyyy", En)}, {TimeFormat.Friendly(TimeSpan.FromMilliseconds(r.BiggestDayMs))}"
                : string.Empty,
            FilterNote = filterNote,
        };
    }

    /// <summary>When each artist was first played, across the whole history under the other filters.</summary>
    private static Dictionary<string, DateTime> FirstHeard(IReadOnlyList<PlayRecord> records, FilterOptions filter)
    {
        var firstHeard = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var record in FilterEngine.Apply(records, filter.WithDateRange(null, null)))
        {
            if (record.Timestamp == DateTime.MinValue)
                continue;
            if (record.Kind != ContentKind.Music && !filter.IncludePodcasts)
                continue;

            string artist = record.DisplayArtist;
            if (!firstHeard.TryGetValue(artist, out var first) || record.Timestamp < first)
                firstHeard[artist] = record.Timestamp;
        }
        return firstHeard;
    }

    /// <summary>"March 4 to 10, 12 hours": the week with the most listening, cut to the range's ends.</summary>
    private static string BiggestWeek(AnalysisResult r, CardRange range, DayOfWeek firstDayOfWeek)
    {
        var weeks = r.PlaytimeByDay
            .GroupBy(p => Weeks.Start(p.Date, firstDayOfWeek))
            .Select(g => (Start: g.Key, Hours: g.Sum(p => p.Value)))
            .ToList();
        if (weeks.Count == 0)
            return string.Empty;

        var best = weeks.MaxBy(w => w.Hours);
        var from = best.Start < range.From ? range.From : best.Start;
        var to = best.Start.AddDays(6) > range.To ? range.To : best.Start.AddDays(6);
        return $"{Days(from, to, withYear: !range.WithinOneYear)}, {HoursInWords((long)(best.Hours * 3_600_000))}";
    }

    // "March 4 to 10", "February 26 to March 3", "December 29, 2024 to January 4, 2025".
    private static string Days(DateTime from, DateTime to, bool withYear)
    {
        if (from == to)
            return from.ToString(withYear ? "MMMM d, yyyy" : "MMMM d", En);
        if (from.Year != to.Year)
            return $"{from.ToString("MMMM d, yyyy", En)} to {to.ToString("MMMM d, yyyy", En)}";

        string end = to.ToString(from.Month == to.Month ? "%d" : "MMMM d", En);
        return withYear
            ? $"{from.ToString("MMMM d", En)} to {end}, {to.Year.ToString(En)}"
            : $"{from.ToString("MMMM d", En)} to {end}";
    }

    private static YearReviewStat Stat(int count, string one, string many) =>
        new(count.ToString("N0", En), count == 1 ? one : many);

    private static string Hours(long ms)
    {
        double hours = ms / 3_600_000d;
        return hours.ToString(hours >= 10 ? "N0" : "0.#", En) + " h";
    }

    private static string HoursInWords(long ms)
    {
        string hours = Hours(ms);
        return hours == "1 h" ? "1 hour" : hours.Replace(" h", " hours");
    }
}
