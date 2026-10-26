using Sortify.Models;

namespace Sortify.Services;

/// <summary>Everything the Compare People tab shows for your history against someone else's.</summary>
public sealed class PeopleComparisonResult
{
    public required AnalysisResult You { get; init; }
    public required AnalysisResult Them { get; init; }

    /// <summary>Your figures in column A, theirs in column B.</summary>
    public IReadOnlyList<ComparisonMetric> Metrics { get; init; } = Array.Empty<ComparisonMetric>();

    /// <summary>How much of both people's listening time goes to the same artists, 0-100.</summary>
    public double ArtistMatch { get; init; }

    /// <summary>The same, for tracks.</summary>
    public double TrackMatch { get; init; }

    public int SharedArtistCount { get; init; }
    public int SharedTrackCount { get; init; }

    /// <summary>Artists both of you play, yours as rank and time A and theirs as B.</summary>
    public IReadOnlyList<ComparisonRow> SharedArtists { get; init; } = Array.Empty<ComparisonRow>();

    /// <summary>Tracks both of you play.</summary>
    public IReadOnlyList<ComparisonRow> SharedTracks { get; init; } = Array.Empty<ComparisonRow>();

    /// <summary>Artists you listen to and they never do.</summary>
    public IReadOnlyList<ComparisonRow> OnlyYou { get; init; } = Array.Empty<ComparisonRow>();

    /// <summary>Artists they listen to and you never do.</summary>
    public IReadOnlyList<ComparisonRow> OnlyThem { get; init; } = Array.Empty<ComparisonRow>();
}

/// <summary>Puts two people's histories side by side, under the same filters.</summary>
public static class PeopleComparison
{
    /// <summary>How many shared artists and tracks the tables show.</summary>
    public const int SharedRows = 50;

    /// <summary>Listening an artist needs from one person to count as theirs alone.</summary>
    public static readonly TimeSpan OnlyOneMinimum = TimeSpan.FromHours(1);

    /// <summary>How many artists each "only" list shows.</summary>
    public const int OnlyOneRows = 25;

    /// <summary>Analyses both histories under the same filters, at the same time, and joins the results.</summary>
    public static async Task<PeopleComparisonResult> CompareAsync(
        IReadOnlyList<PlayRecord> yours, IReadOnlyList<PlayRecord> theirs, FilterOptions filter,
        TimeSpan? sessionGap = null, CancellationToken cancellationToken = default)
    {
        var you = AnalysisEngine.AnalyzeAsync(yours, filter, sessionGap, cancellationToken);
        var them = AnalysisEngine.AnalyzeAsync(theirs, filter, sessionGap, cancellationToken);
        await Task.WhenAll(you, them).ConfigureAwait(false);
        return Build(you.Result, them.Result);
    }

    public static PeopleComparisonResult Build(AnalysisResult you, AnalysisResult them)
    {
        var artists = Join(you.Artists, them.Artists, x => (x.Artist, string.Empty), x => x.TotalMsPlayed);
        var tracks = Join(you.Tracks, them.Tracks, x => (x.Track, x.Artist), x => x.TotalMsPlayed);

        return new PeopleComparisonResult
        {
            You = you,
            Them = them,
            Metrics = Metrics(you, them),
            ArtistMatch = Match(artists.Shared, you.TotalMsPlayed, them.TotalMsPlayed),
            TrackMatch = Match(tracks.Shared, you.TotalMsPlayed, them.TotalMsPlayed),
            SharedArtistCount = artists.Shared.Count,
            SharedTrackCount = tracks.Shared.Count,
            SharedArtists = Ordered(artists.Shared, you.TotalMsPlayed, them.TotalMsPlayed).Take(SharedRows).ToList(),
            SharedTracks = Ordered(tracks.Shared, you.TotalMsPlayed, them.TotalMsPlayed).Take(SharedRows).ToList(),
            OnlyYou = artists.OnlyYou,
            OnlyThem = artists.OnlyThem,
        };
    }

    /// <summary>
    /// Pairs up what both people play, and picks out the top of what only one of them does.
    /// Ranks come from each person's full list. A shared row carries your stat, so
    /// double-clicking it opens your own breakdown.
    /// </summary>
    private static (List<ComparisonRow> Shared, List<ComparisonRow> OnlyYou, List<ComparisonRow> OnlyThem) Join<T>(
        IReadOnlyList<T> you, IReadOnlyList<T> them, Func<T, (string Name, string Secondary)> key, Func<T, long> ms)
        where T : class
    {
        var theirRank = new Dictionary<(string, string), (int Rank, T Item)>();
        for (int i = 0; i < them.Count; i++)
            theirRank.TryAdd(key(them[i]), (i + 1, them[i]));
        var yourKeys = new HashSet<(string, string)>();

        long minimum = (long)OnlyOneMinimum.TotalMilliseconds;
        var shared = new List<ComparisonRow>();
        var onlyYou = new List<ComparisonRow>();
        for (int i = 0; i < you.Count; i++)
        {
            var k = key(you[i]);
            if (!yourKeys.Add(k))
                continue;
            if (theirRank.TryGetValue(k, out var theirs))
                shared.Add(new ComparisonRow(k.Name, k.Secondary, i + 1, theirs.Rank, ms(you[i]), ms(theirs.Item), you[i]));
            else if (onlyYou.Count < OnlyOneRows && ms(you[i]) >= minimum)
                onlyYou.Add(new ComparisonRow(k.Name, k.Secondary, i + 1, null, ms(you[i]), 0, you[i]));
        }

        var onlyThem = theirRank
            .Where(kv => !yourKeys.Contains(kv.Key) && ms(kv.Value.Item) >= minimum)
            .OrderBy(kv => kv.Value.Rank)
            .Take(OnlyOneRows)
            .Select(kv => new ComparisonRow(kv.Key.Item1, kv.Key.Item2, null, kv.Value.Rank, 0, ms(kv.Value.Item), kv.Value.Item))
            .ToList();

        return (shared, onlyYou, onlyThem);
    }

    /// <summary>
    /// The share of listening the two of you have in common: for everything you both play,
    /// the smaller of your two shares of total listening time, added up. Two identical
    /// histories score 100%, two with nothing in common 0%.
    /// </summary>
    internal static double Match(IEnumerable<ComparisonRow> shared, long yourTotal, long theirTotal)
    {
        if (yourTotal <= 0 || theirTotal <= 0)
            return 0;
        double sum = shared.Sum(r => Math.Min((double)r.MsA / yourTotal, (double)r.MsB / theirTotal));
        return Math.Clamp(sum * 100, 0, 100);
    }

    /// <summary>What you have most in common first: the larger the smaller of your two shares, the higher.</summary>
    private static IEnumerable<ComparisonRow> Ordered(IEnumerable<ComparisonRow> shared, long yourTotal, long theirTotal)
    {
        double You(ComparisonRow r) => yourTotal <= 0 ? 0 : (double)r.MsA / yourTotal;
        double Them(ComparisonRow r) => theirTotal <= 0 ? 0 : (double)r.MsB / theirTotal;
        return shared
            .OrderByDescending(r => Math.Min(You(r), Them(r)))
            .ThenByDescending(r => You(r) + Them(r))
            .ThenBy(r => r.Name, StringComparer.Ordinal);
    }

    private static List<ComparisonMetric> Metrics(AnalysisResult you, AnalysisResult them)
    {
        double PerDay(AnalysisResult r) => r.ActiveDays == 0 ? 0 : (double)r.TotalMsPlayed / r.ActiveDays;
        double SkipRate(AnalysisResult r) => r.SkipEligiblePlays == 0 ? 0 : r.TotalSkips * 100.0 / r.SkipEligiblePlays;
        string Time(double ms) => TimeFormat.Friendly(TimeSpan.FromMilliseconds(ms));
        string Count(double n) => n.ToString("N0");

        var metrics = new List<ComparisonMetric>
        {
            PeriodComparison.Numeric("Listening time", you.TotalMsPlayed, them.TotalMsPlayed, Time, people: true),
            PeriodComparison.Numeric("Plays", you.TotalPlays, them.TotalPlays, Count, people: true),
            PeriodComparison.Numeric("Artists", you.UniqueArtists, them.UniqueArtists, Count, people: true),
            PeriodComparison.Numeric("Tracks", you.UniqueTracks, them.UniqueTracks, Count, people: true),
            PeriodComparison.Numeric("Days with listening", you.ActiveDays, them.ActiveDays, Count, people: true),
            PeriodComparison.Numeric("Average per listening day", PerDay(you), PerDay(them), Time, people: true),
        };

        metrics.Add(PeriodComparison.Points("Skip rate", SkipRate(you), SkipRate(them)));

        string topArtistYou = you.Artists.Count > 0 ? you.Artists[0].Artist : "-";
        string topArtistThem = them.Artists.Count > 0 ? them.Artists[0].Artist : "-";
        metrics.Add(Same("Top artist", topArtistYou, topArtistThem));

        string topTrackYou = you.Tracks.Count > 0 ? $"{you.Tracks[0].Track} - {you.Tracks[0].Artist}" : "-";
        string topTrackThem = them.Tracks.Count > 0 ? $"{them.Tracks[0].Track} - {them.Tracks[0].Artist}" : "-";
        metrics.Add(Same("Top track", topTrackYou, topTrackThem));
        return metrics;
    }

    private static ComparisonMetric Same(string name, string you, string them) =>
        you == them && you != "-"
            ? new ComparisonMetric(name, you, them, "same", Trend.Up)
            : new ComparisonMetric(name, you, them, "different", Trend.Same);
}
