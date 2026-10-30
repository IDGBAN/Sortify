using Sortify.Models;

namespace Sortify.Services;

/// <summary>Each year's listening, split by the year its tracks were first heard.</summary>
public sealed class CohortBreakdown
{
    /// <summary>The years with listening, oldest first.</summary>
    public IReadOnlyList<int> Years { get; init; } = Array.Empty<int>();

    /// <summary>The years tracks were first heard in, oldest first.</summary>
    public IReadOnlyList<int> Cohorts { get; init; } = Array.Empty<int>();

    /// <summary>Hours played in <c>Years[i]</c> of tracks first heard in <c>Cohorts[j]</c>.</summary>
    public double[,] Hours { get; init; } = new double[0, 0];

    /// <summary>Plays in <c>Years[i]</c> of tracks first heard in <c>Cohorts[j]</c>.</summary>
    public int[,] Plays { get; init; } = new int[0, 0];

    public static CohortBreakdown Empty { get; } = new();

    /// <summary>The hours, or plays, in <c>Years[y]</c> of tracks first heard in <c>Cohorts[c]</c>.</summary>
    public double Amount(int y, int c, Metric metric) => metric == Metric.Plays ? Plays[y, c] : Hours[y, c];

    /// <summary>The share (0-100) of a year's listening time, or plays, that went to tracks first heard that same year.</summary>
    public double NewSharePercent(int year, Metric metric = Metric.Time) => SharePercent(year, year, metric);

    /// <summary>
    /// The share (0-100) of <paramref name="year"/>'s listening time, or plays, that went to
    /// tracks first heard in <paramref name="cohort"/>.
    /// </summary>
    public double SharePercent(int year, int cohort, Metric metric = Metric.Time)
    {
        int y = IndexOf(Years, year), c = IndexOf(Cohorts, cohort);
        if (y < 0 || c < 0)
            return 0;

        double total = 0;
        for (int j = 0; j < Cohorts.Count; j++)
            total += Amount(y, j, metric);
        return total > 0 ? Amount(y, c, metric) * 100 / total : 0;
    }

    private static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
                return i;
        }
        return -1;
    }
}

/// <summary>Works out a <see cref="CohortBreakdown"/>.</summary>
public static class DiscoveryCohorts
{
    /// <summary>
    /// How many years back the chart gives a band of its own: music found that year, the year
    /// before, and so on. Anything found longer ago shares one "earlier" band. 2 to 5 read well;
    /// past that the colours get hard to tell apart.
    /// </summary>
    public const int YearsBack = 3;

    public static Task<CohortBreakdown> BuildAsync(
        IReadOnlyList<PlayRecord> records, FilterOptions filter, CancellationToken cancellationToken = default)
        => Task.Run(() => Build(records, filter, cancellationToken), cancellationToken);

    /// <summary>
    /// When a track was first heard is judged against the whole history under the other
    /// filters, the date range set aside, as the year card judges new artists. So with the
    /// sidebar narrowed to one year, music found before it still counts as older.
    /// </summary>
    public static CohortBreakdown Build(IReadOnlyList<PlayRecord> records, FilterOptions filter, CancellationToken cancellationToken = default)
    {
        var firstHeard = new Dictionary<(string Track, string Artist), DateTime>();
        int counter = 0;
        foreach (var r in FilterEngine.Apply(records, filter.WithDateRange(null, null)))
        {
            if ((++counter & 0x3FFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (!Counts(r, filter))
                continue;

            var key = (r.DisplayTrack, r.DisplayArtist);
            if (!firstHeard.TryGetValue(key, out var first) || r.Timestamp < first)
                firstHeard[key] = r.Timestamp;
        }

        var cells = new Dictionary<(int Year, int Cohort), (long Ms, int Plays)>();
        foreach (var r in FilterEngine.Apply(records, filter))
        {
            if ((++counter & 0x3FFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (!Counts(r, filter))
                continue;

            int cohort = firstHeard[(r.DisplayTrack, r.DisplayArtist)].Year;
            var cell = (r.Timestamp.Year, cohort);
            var (ms, plays) = cells.GetValueOrDefault(cell);
            cells[cell] = (ms + r.MsPlayed, plays + 1);
        }

        if (cells.Count == 0)
            return CohortBreakdown.Empty;

        var years = cells.Keys.Select(k => k.Year).Distinct().Order().ToList();
        var cohorts = cells.Keys.Select(k => k.Cohort).Distinct().Order().ToList();
        var hours = new double[years.Count, cohorts.Count];
        var playCounts = new int[years.Count, cohorts.Count];
        foreach (var ((year, cohort), value) in cells)
        {
            int y = years.IndexOf(year), c = cohorts.IndexOf(cohort);
            hours[y, c] = value.Ms / 3_600_000d;
            playCounts[y, c] = value.Plays;
        }

        return new CohortBreakdown { Years = years, Cohorts = cohorts, Hours = hours, Plays = playCounts };
    }

    /// <summary>A dated play the year rollups count: music, or podcasts when the sidebar includes them.</summary>
    private static bool Counts(PlayRecord r, FilterOptions filter) =>
        r.Timestamp != DateTime.MinValue && (r.Kind == ContentKind.Music || filter.IncludePodcasts);
}
