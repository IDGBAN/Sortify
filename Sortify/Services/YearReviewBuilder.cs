using System.Globalization;
using Sortify.Models;

namespace Sortify.Services;

/// <summary>Works out what goes on a year-in-review card.</summary>
public static class YearReviewBuilder
{
    /// <summary>How many artists and tracks the card lists.</summary>
    public const int TopCount = 5;

    public static Task<YearReview> BuildAsync(
        IReadOnlyList<PlayRecord> records, FilterOptions filter, int year, string filterNote,
        TimeSpan? sessionGap = null, CancellationToken cancellationToken = default)
        => Task.Run(() => Build(records, filter, year, filterNote, sessionGap, cancellationToken), cancellationToken);

    /// <summary>
    /// The year under the sidebar filters, whatever date range the sidebar has. Whether an
    /// artist is new that year is judged against the whole history, not just the year.
    /// </summary>
    public static YearReview Build(
        IReadOnlyList<PlayRecord> records, FilterOptions filter, int year, string filterNote,
        TimeSpan? sessionGap = null, CancellationToken cancellationToken = default)
    {
        var start = new DateTime(year, 1, 1);
        var r = AnalysisEngine.Analyze(records, filter.WithDateRange(start, start.AddYears(1).AddTicks(-1)), sessionGap, cancellationToken);

        var firstHeard = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var record in FilterEngine.Apply(records, filter.WithDateRange(null, null)))
        {
            if (record.Timestamp == DateTime.MinValue)
                continue;
            bool isMusic = record.Kind == ContentKind.Music;
            if (!isMusic && !filter.IncludePodcasts)
                continue;

            string artist = isMusic ? record.ArtistName : record.ShowName;
            if (!firstHeard.TryGetValue(artist, out var first) || record.Timestamp < first)
                firstHeard[artist] = record.Timestamp;
        }

        var biggestMonth = r.Months.MaxBy(m => m.TotalMsPlayed);
        return new YearReview
        {
            Year = year,
            TotalMsPlayed = r.TotalMsPlayed,
            Plays = r.TotalPlays,
            Artists = r.UniqueArtists,
            Tracks = r.UniqueTracks,
            NewArtists = firstHeard.Values.Count(d => d.Year == year),
            LongestStreakDays = r.LongestStreakDays,
            TopArtists = r.Artists.Take(TopCount)
                .Select((a, i) => new YearReviewEntry(i + 1, a.Artist, string.Empty, Hours(a.TotalMsPlayed)))
                .ToList(),
            TopTracks = r.Tracks.Take(TopCount)
                .Select((t, i) => new YearReviewEntry(i + 1, t.Track, t.Artist, Hours(t.TotalMsPlayed)))
                .ToList(),
            BiggestMonth = biggestMonth?.Month,
            BiggestMonthMs = biggestMonth?.TotalMsPlayed ?? 0,
            BiggestDay = r.BiggestDay,
            BiggestDayMs = r.BiggestDayMs,
            BiggestMonthText = biggestMonth is { } m
                ? $"{m.Month.ToString("MMMM", CultureInfo.InvariantCulture)}, {Hours(m.TotalMsPlayed).Replace(" h", " hours")}"
                : string.Empty,
            BiggestDayText = r.BiggestDay is { } d
                ? $"{d.ToString("MMMM d", CultureInfo.InvariantCulture)}, {TimeFormat.Friendly(TimeSpan.FromMilliseconds(r.BiggestDayMs))}"
                : string.Empty,
            FilterNote = filterNote,
        };
    }

    private static string Hours(long ms)
    {
        double hours = ms / 3_600_000d;
        return hours >= 10 ? $"{hours:N0} h" : $"{hours:0.#} h";
    }
}
