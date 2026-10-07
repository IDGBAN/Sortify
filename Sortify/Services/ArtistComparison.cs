using Sortify.Models;

namespace Sortify.Services;

/// <summary>Listening time per month (or week) for a few chosen artists, on one shared timeline.</summary>
public sealed class ArtistTimeline
{
    /// <summary>The start of every bucket from the first play to the last, with none skipped.</summary>
    public IReadOnlyList<DateTime> Buckets { get; init; } = Array.Empty<DateTime>();

    /// <summary>Hours per bucket for each artist, in the order they were asked for.</summary>
    public IReadOnlyList<(string Artist, double[] Hours)> Series { get; init; } = Array.Empty<(string, double[])>();

    public static ArtistTimeline Empty { get; } = new();
}

/// <summary>Builds <see cref="ArtistTimeline"/>s for the artist comparison chart on the Trends tab.</summary>
public static class ArtistComparison
{
    /// <summary>How many artists the chart compares at once; more lines than this get hard to tell apart.</summary>
    public const int MaxArtists = 5;

    public static Task<ArtistTimeline> BuildAsync(
        IReadOnlyList<PlayRecord> records, FilterOptions filter, IReadOnlyList<string> artists,
        ChartBuilder.TimeGranularity granularity, DayOfWeek firstDay = DayOfWeek.Sunday,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Build(records, filter, artists, granularity, firstDay, cancellationToken), cancellationToken);

    /// <summary>
    /// One filtered pass over the plays of the given artists. A bucket an artist has no plays
    /// in counts as zero rather than being left out, so a line drops to the floor in a quiet
    /// month instead of drawing straight across it.
    /// </summary>
    /// <param name="firstDay">The day a weekly bucket starts on.</param>
    public static ArtistTimeline Build(
        IReadOnlyList<PlayRecord> records, FilterOptions filter, IReadOnlyList<string> artists,
        ChartBuilder.TimeGranularity granularity, DayOfWeek firstDay = DayOfWeek.Sunday,
        CancellationToken cancellationToken = default)
    {
        if (artists.Count == 0)
            return ArtistTimeline.Empty;

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var artist in artists)
            index.TryAdd(artist, index.Count);
        var perArtist = index.Keys.Select(_ => new Dictionary<DateTime, long>()).ToArray();

        DateTime? first = null, last = null;
        int counter = 0;
        foreach (var r in FilterEngine.Apply(records, filter))
        {
            if ((++counter & 0x3FFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            if (r.Timestamp == DateTime.MinValue)
                continue;

            if (r.Kind != ContentKind.Music && !filter.IncludePodcasts)
                continue;
            if (!index.TryGetValue(r.DisplayArtist, out int i))
                continue;

            var bucket = TimeBuckets.Of(r.Timestamp, granularity, firstDay);
            perArtist[i][bucket] = perArtist[i].GetValueOrDefault(bucket) + r.MsPlayed;
            if (first is null || bucket < first) first = bucket;
            if (last is null || bucket > last) last = bucket;
        }

        if (first is not { } from || last is not { } to)
            return new ArtistTimeline { Series = index.Keys.Select(a => (a, Array.Empty<double>())).ToList() };

        var buckets = TimeBuckets.Between(from, to, granularity, firstDay);

        var series = index.Keys
            .Select((artist, i) => (artist, buckets
                .Select(b => Math.Round(perArtist[i].GetValueOrDefault(b) / 3_600_000d, 2))
                .ToArray()))
            .ToList();

        return new ArtistTimeline { Buckets = buckets, Series = series };
    }
}
