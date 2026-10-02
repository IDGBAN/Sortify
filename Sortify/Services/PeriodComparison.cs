using Sortify.Models;

namespace Sortify.Services;

/// <summary>How something moved between the first period and the second.</summary>
public enum Trend
{
    Same,
    Up,
    Down,
    New,
    Gone,
}

/// <summary>One artist or track, ranked by listening time in each period.</summary>
public sealed record ComparisonRow(string Name, string Secondary, int? RankA, int? RankB, long MsA, long MsB, object Item)
{
    public double HoursA => MsA / 3_600_000d;
    public double HoursB => MsB / 3_600_000d;

    public Trend Trend => (RankA, RankB) switch
    {
        (null, _) => Trend.New,
        (_, null) => Trend.Gone,
        ({ } a, { } b) when b < a => Trend.Up,
        ({ } a, { } b) when b > a => Trend.Down,
        _ => Trend.Same,
    };

    /// <summary>"▲ 4", "▼ 2", "new", "out" or "=", for the movement column.</summary>
    public string Movement => Trend switch
    {
        Trend.Up => $"▲ {RankA - RankB}",
        Trend.Down => $"▼ {RankB - RankA}",
        Trend.New => "new",
        Trend.Gone => "out",
        _ => "=",
    };
}

/// <summary>One headline figure for both periods and how it changed.</summary>
public sealed record ComparisonMetric(string Metric, string A, string B, string Change, Trend Trend);

/// <summary>Everything the Compare tab shows for one pair of periods.</summary>
public sealed class PeriodComparisonResult
{
    public required AnalysisResult A { get; init; }
    public required AnalysisResult B { get; init; }
    public IReadOnlyList<ComparisonMetric> Metrics { get; init; } = Array.Empty<ComparisonMetric>();
    public IReadOnlyList<ComparisonRow> Artists { get; init; } = Array.Empty<ComparisonRow>();
    public IReadOnlyList<ComparisonRow> Tracks { get; init; } = Array.Empty<ComparisonRow>();

    /// <summary>Artists listened to in the second period and not at all in the first.</summary>
    public IReadOnlyList<ComparisonRow> NewInB { get; init; } = Array.Empty<ComparisonRow>();

    /// <summary>Artists listened to in the first period and not at all in the second.</summary>
    public IReadOnlyList<ComparisonRow> GoneSinceA { get; init; } = Array.Empty<ComparisonRow>();
}

/// <summary>Puts two date ranges of the same history side by side.</summary>
public static class PeriodComparison
{
    /// <summary>How many of each period's top artists and tracks the ranking tables take in.</summary>
    public const int RowsPerTable = 50;

    /// <summary>Listening an artist needs in a period to count as new in it, or gone from the other.</summary>
    public static readonly TimeSpan NewOrGoneMinimum = TimeSpan.FromHours(1);

    /// <summary>How many artists the new and gone lists show.</summary>
    public const int NewOrGoneRows = 25;

    /// <summary>
    /// Analyses both periods under the same filters, apart from the dates, and joins the
    /// results. The two analyses run at the same time.
    /// </summary>
    public static async Task<PeriodComparisonResult> CompareAsync(
        IReadOnlyList<PlayRecord> records, FilterOptions filter,
        DateTime? startA, DateTime? endA, DateTime? startB, DateTime? endB,
        TimeSpan? sessionGap = null, CancellationToken cancellationToken = default)
    {
        var a = AnalysisEngine.AnalyzeAsync(records, filter.WithDateRange(startA, EndOfDay(endA)), sessionGap, cancellationToken);
        var b = AnalysisEngine.AnalyzeAsync(records, filter.WithDateRange(startB, EndOfDay(endB)), sessionGap, cancellationToken);
        await Task.WhenAll(a, b).ConfigureAwait(false);
        return Build(a.Result, b.Result);
    }

    /// <summary>A period's last day is included in full, as the sidebar's To date is.</summary>
    private static DateTime? EndOfDay(DateTime? end) => end?.Date.AddDays(1).AddTicks(-1);

    public static PeriodComparisonResult Build(AnalysisResult a, AnalysisResult b)
    {
        var artists = Rows(a.Artists, b.Artists, x => (x.Artist, string.Empty), x => x.TotalMsPlayed);
        var tracks = Rows(a.Tracks, b.Tracks, x => (x.Track, x.Artist), x => x.TotalMsPlayed);

        var inA = a.Artists.Select(x => x.Artist).ToHashSet(StringComparer.Ordinal);
        var inB = b.Artists.Select(x => x.Artist).ToHashSet(StringComparer.Ordinal);
        long minimum = (long)NewOrGoneMinimum.TotalMilliseconds;

        var newInB = b.Artists
            .Select((x, i) => (x, Rank: i + 1))
            .Where(p => !inA.Contains(p.x.Artist) && p.x.TotalMsPlayed >= minimum)
            .Take(NewOrGoneRows)
            .Select(p => new ComparisonRow(p.x.Artist, string.Empty, null, p.Rank, 0, p.x.TotalMsPlayed, p.x))
            .ToList();
        var goneSinceA = a.Artists
            .Select((x, i) => (x, Rank: i + 1))
            .Where(p => !inB.Contains(p.x.Artist) && p.x.TotalMsPlayed >= minimum)
            .Take(NewOrGoneRows)
            .Select(p => new ComparisonRow(p.x.Artist, string.Empty, p.Rank, null, p.x.TotalMsPlayed, 0, p.x))
            .ToList();

        return new PeriodComparisonResult
        {
            A = a,
            B = b,
            Metrics = Metrics(a, b),
            Artists = artists,
            Tracks = tracks,
            NewInB = newInB,
            GoneSinceA = goneSinceA,
        };
    }

    /// <summary>
    /// The top of each period, joined. Ranks come from the full lists, so an artist outside
    /// one period's top rows still shows how far they climbed or fell. Ordered by the second
    /// period's rank, with anything that dropped out of it at the bottom.
    /// </summary>
    private static List<ComparisonRow> Rows<T>(
        IReadOnlyList<T> a, IReadOnlyList<T> b, Func<T, (string Name, string Secondary)> key, Func<T, long> ms)
        where T : class
    {
        var rankA = new Dictionary<(string, string), (int Rank, T Item)>();
        for (int i = 0; i < a.Count; i++)
            rankA.TryAdd(key(a[i]), (i + 1, a[i]));
        var rankB = new Dictionary<(string, string), (int Rank, T Item)>();
        for (int i = 0; i < b.Count; i++)
            rankB.TryAdd(key(b[i]), (i + 1, b[i]));

        return b.Take(RowsPerTable).Select(key)
            .Concat(a.Take(RowsPerTable).Select(key))
            .Distinct()
            .Select(k =>
            {
                bool hasA = rankA.TryGetValue(k, out var inA);
                bool hasB = rankB.TryGetValue(k, out var inB);
                return new ComparisonRow(k.Item1, k.Item2,
                    hasA ? inA.Rank : null, hasB ? inB.Rank : null,
                    hasA ? ms(inA.Item) : 0, hasB ? ms(inB.Item) : 0,
                    hasB ? inB.Item : inA.Item);
            })
            .OrderBy(r => r.RankB ?? int.MaxValue)
            .ThenBy(r => r.RankA ?? int.MaxValue)
            .ToList();
    }

    private static List<ComparisonMetric> Metrics(AnalysisResult a, AnalysisResult b)
    {
        double PerDay(AnalysisResult r) => r.ActiveDays == 0 ? 0 : (double)r.TotalMsPlayed / r.ActiveDays;
        double SkipRate(AnalysisResult r) => r.SkipEligiblePlays == 0 ? 0 : r.TotalSkips * 100.0 / r.SkipEligiblePlays;

        var metrics = new List<ComparisonMetric>
        {
            Numeric("Listening time", a.TotalMsPlayed, b.TotalMsPlayed, ms => TimeFormat.Friendly(TimeSpan.FromMilliseconds(ms))),
            Numeric("Plays", a.TotalPlays, b.TotalPlays, n => n.ToString("N0")),
            Numeric("Artists", a.UniqueArtists, b.UniqueArtists, n => n.ToString("N0")),
            Numeric("Tracks", a.UniqueTracks, b.UniqueTracks, n => n.ToString("N0")),
            Numeric("Days with listening", a.ActiveDays, b.ActiveDays, n => n.ToString("N0")),
            Numeric("Average per listening day", PerDay(a), PerDay(b), ms => TimeFormat.Friendly(TimeSpan.FromMilliseconds(ms))),
        };

        double skipA = SkipRate(a), skipB = SkipRate(b);
        metrics.Add(new ComparisonMetric("Skip rate", $"{skipA:0.#}%", $"{skipB:0.#}%",
            $"{skipB - skipA:+0.#;-0.#;0} points", Direction(skipA, skipB)));

        string topA = a.Artists.Count > 0 ? a.Artists[0].Artist : "-";
        string topB = b.Artists.Count > 0 ? b.Artists[0].Artist : "-";
        metrics.Add(new ComparisonMetric("Top artist", topA, topB,
            topA == topB ? "same" : "changed", topA == topB ? Trend.Same : Trend.New));
        return metrics;
    }

    private static ComparisonMetric Numeric(string name, double a, double b, Func<double, string> format)
    {
        string change = a == 0
            ? b == 0 ? "no change" : "new"
            : $"{(b - a) * 100 / a:+0;-0;0}%";
        return new ComparisonMetric(name, format(a), format(b), change, a == 0 && b > 0 ? Trend.New : Direction(a, b));
    }

    private static Trend Direction(double a, double b) => b > a ? Trend.Up : b < a ? Trend.Down : Trend.Same;
}
