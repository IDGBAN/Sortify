using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Sortify.Models;

namespace Sortify.Services;

/// <summary>Exports analysis results to TXT, CSV, Markdown and JSON.</summary>
public static class ExportService
{
    /// <summary>How many rows each ranked table in the Markdown report shows.</summary>
    private const int MarkdownTopN = 25;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Track and artist names are full of characters the default (HTML-safe) encoder
        // escapes into \uXXXX, which makes the file unreadable for a human.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The line saying which filters shaped a report. Filter descriptions can contain commas
    /// ("Sun, Sat"), so they are separated with semicolons.
    /// </summary>
    internal static string FiltersLine(IReadOnlyList<string>? filters) =>
        filters is { Count: > 0 }
            ? "Filters: " + string.Join("; ", filters)
            : "Filters: none (every play in the export)";

    /// <param name="filters">The filters the result was computed under, as the chip row words them.</param>
    public static async Task SaveTxtAsync(string path, AnalysisResult result, IReadOnlyList<string>? filters = null)
    {
        var sb = new StringBuilder();

        // ---- Summary -------------------------------------------------------------------
        sb.AppendLine("Sortify Results");
        sb.AppendLine($"Generated: {TimeFormat.Timestamp(DateTime.Now)}");
        sb.AppendLine(FiltersLine(filters));
        sb.AppendLine();
        sb.AppendLine($"Total listening time: {TimeFormat.Friendly(result.TotalTime)} ({TimeFormat.HhMmSs(result.TotalTime)})");
        sb.AppendLine($"Total plays: {result.TotalPlays:N0}");
        sb.AppendLine($"Unique tracks: {result.UniqueTracks:N0}");
        sb.AppendLine($"Unique artists: {result.UniqueArtists:N0}");
        sb.AppendLine($"Unique albums: {result.UniqueAlbums:N0}");
        if (result.FirstListen is { } first && result.LastListen is { } last)
            sb.AppendLine($"Date range: {TimeFormat.Timestamp(first)} to {TimeFormat.Timestamp(last)}");
        if (result.ActiveDays > 0)
            sb.AppendLine($"Active days: {result.ActiveDays:N0}");
        if (result.LongestStreakDays > 0 && result.LongestStreakStart is { } ss && result.LongestStreakEnd is { } se)
            sb.AppendLine($"Longest streak: {result.LongestStreakDays} days ({ss:yyyy-MM-dd} to {se:yyyy-MM-dd})");
        if (result.LongestBreakDays > 0 && result.LongestBreakStart is { } bs && result.LongestBreakEnd is { } be)
            sb.AppendLine($"Longest break: {result.LongestBreakDays} days ({bs:yyyy-MM-dd} to {be:yyyy-MM-dd})");
        if (result.BiggestDay is { } bd)
            sb.AppendLine($"Biggest day: {bd:yyyy-MM-dd} ({TimeFormat.Friendly(TimeSpan.FromMilliseconds(result.BiggestDayMs))})");
        if (result.SessionCount > 0)
            sb.AppendLine($"Listening sessions: {result.SessionCount:N0} (avg {TimeFormat.Friendly(TimeSpan.FromMilliseconds(result.AvgSessionMs))})");
        if (result.SkipEligiblePlays > 0)
        {
            sb.AppendLine($"Skip rate: {result.TotalSkips * 100.0 / result.SkipEligiblePlays:0.#}%");
            sb.AppendLine($"Completion rate: {result.CompletedPlays * 100.0 / result.SkipEligiblePlays:0.#}%");
        }
        if (result.UniqueTracks > 0)
            sb.AppendLine($"Plays per track: {result.PlaysPerTrack:0.0}");
        sb.AppendLine();

        // ---- Rankings ------------------------------------------------------------------
        // Tracks/Artists/Albums arrive pre-sorted by listening time; the play-count and
        // first-listen orderings come from the result's pre-sorted views where available.
        sb.AppendLine("Tracks Ranked by Time Listened:");
        int rank = 1;
        foreach (var t in result.Tracks)
            sb.AppendLine($"{rank++}. {TimeFormat.HhMmSs(t.TotalTime)} - {t.Track} - {t.Artist}");
        sb.AppendLine();

        sb.AppendLine("Tracks Ranked by Counts Listened:");
        rank = 1;
        foreach (var t in result.TracksByPlayCount)
            sb.AppendLine($"{rank++}. {t.PlayCount} times - {t.Track} - {t.Artist}");
        sb.AppendLine();

        sb.AppendLine("Tracks Ranked by First Time Listened:");
        rank = 1;
        foreach (var t in result.Tracks
                     .Where(t => t.FirstPlayed is not null)
                     .OrderBy(t => t.FirstPlayed))
            sb.AppendLine($"{rank++}. {TimeFormat.Timestamp(t.FirstPlayed)} - {t.Track} - {t.Artist}");
        sb.AppendLine();

        sb.AppendLine("Artists Ranked by Time Listened:");
        rank = 1;
        foreach (var a in result.Artists)
            sb.AppendLine($"{rank++}. {TimeFormat.HhMmSs(a.TotalTime)} - {a.Artist}");
        sb.AppendLine();

        sb.AppendLine("Albums Ranked by Time Listened:");
        rank = 1;
        foreach (var a in result.Albums)
            sb.AppendLine($"{rank++}. {TimeFormat.HhMmSs(a.TotalTime)} - {a.Album} - {a.Artist}");
        sb.AppendLine();

        if (result.Years.Count > 0)
        {
            sb.AppendLine("Listening by Year:");
            foreach (var y in result.Years)
                sb.AppendLine($"{y.Year}: {TimeFormat.HhMmSs(y.TotalTime)} across {y.PlayCount:N0} plays - top artist: {y.TopArtist} - top track: {y.TopTrack}");
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);
    }

    public static async Task SaveTracksCsvAsync(string path, AnalysisResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Rank,Track,Artist,TotalTime,Hours,PlayCount,FirstPlayed,LastPlayed");
        int rank = 1;
        foreach (var t in result.Tracks)
        {
            sb.Append(rank++).Append(',')
              .Append(Csv(t.Track)).Append(',')
              .Append(Csv(t.Artist)).Append(',')
              .Append(TimeFormat.HhMmSs(t.TotalTime)).Append(',')
              .Append(t.TotalHours.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
              .Append(t.PlayCount).Append(',')
              .Append(TimeFormat.Timestamp(t.FirstPlayed)).Append(',')
              .Append(TimeFormat.Timestamp(t.LastPlayed))
              .AppendLine();
        }
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);
    }

    public static async Task SaveArtistsCsvAsync(string path, AnalysisResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Rank,Artist,TotalTime,Hours,PlayCount,FirstPlayed,FirstTrack,LastPlayed");
        int rank = 1;
        foreach (var a in result.Artists)
        {
            sb.Append(rank++).Append(',')
              .Append(Csv(a.Artist)).Append(',')
              .Append(TimeFormat.HhMmSs(a.TotalTime)).Append(',')
              .Append(a.TotalHours.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
              .Append(a.PlayCount).Append(',')
              .Append(TimeFormat.Timestamp(a.FirstPlayed)).Append(',')
              .Append(Csv(a.FirstTrack)).Append(',')
              .Append(TimeFormat.Timestamp(a.LastPlayed))
              .AppendLine();
        }
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);
    }

    public static async Task SaveAlbumsCsvAsync(string path, AnalysisResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Rank,Album,Artist,TotalTime,Hours,PlayCount,FirstPlayed,LastPlayed");
        int rank = 1;
        foreach (var a in result.Albums)
        {
            sb.Append(rank++).Append(',')
              .Append(Csv(a.Album)).Append(',')
              .Append(Csv(a.Artist)).Append(',')
              .Append(TimeFormat.HhMmSs(a.TotalTime)).Append(',')
              .Append(a.TotalHours.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
              .Append(a.PlayCount).Append(',')
              .Append(TimeFormat.Timestamp(a.FirstPlayed)).Append(',')
              .Append(TimeFormat.Timestamp(a.LastPlayed))
              .AppendLine();
        }
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);
    }

    public static async Task SaveYearsCsvAsync(string path, AnalysisResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Year,TotalTime,Hours,PlayCount,UniqueArtists,UniqueTracks,TopArtist,TopTrack");
        foreach (var y in result.Years)
        {
            sb.Append(y.Year).Append(',')
              .Append(TimeFormat.HhMmSs(y.TotalTime)).Append(',')
              .Append(y.TotalHours.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
              .Append(y.PlayCount).Append(',')
              .Append(y.UniqueArtists).Append(',')
              .Append(y.UniqueTracks).Append(',')
              .Append(Csv(y.TopArtist)).Append(',')
              .Append(Csv(y.TopTrack))
              .AppendLine();
        }
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);
    }

    public static async Task SaveShowsCsvAsync(string path, AnalysisResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Rank,Show,Kind,TotalTime,Hours,PlayCount,Episodes,FirstPlayed,LastPlayed");
        int rank = 1;
        foreach (var s in result.Shows)
        {
            sb.Append(rank++).Append(',')
              .Append(Csv(s.Show)).Append(',')
              .Append(s.Kind).Append(',')
              .Append(TimeFormat.HhMmSs(s.TotalTime)).Append(',')
              .Append(s.TotalHours.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
              .Append(s.PlayCount).Append(',')
              .Append(s.EpisodeCount).Append(',')
              .Append(TimeFormat.Timestamp(s.FirstPlayed)).Append(',')
              .Append(TimeFormat.Timestamp(s.LastPlayed))
              .AppendLine();
        }
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);
    }

    // ---- Markdown ------------------------------------------------------------------------

    /// <summary>
    /// A report meant to be read: the summary and insights in full, then the top
    /// <see cref="MarkdownTopN"/> of each ranking as tables.
    /// </summary>
    public static async Task SaveMarkdownAsync(string path, AnalysisResult r, IReadOnlyList<string>? filters = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Sortify report").AppendLine();
        sb.AppendLine($"*Generated {TimeFormat.Timestamp(DateTime.Now)}*").AppendLine();
        sb.AppendLine(MdText(FiltersLine(filters))).AppendLine();

        sb.AppendLine("## Summary").AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| --- | --- |");
        Row("Total listening time", $"{TimeFormat.Friendly(r.TotalTime)} ({TimeFormat.HhMmSs(r.TotalTime)})");
        Row("Total plays", r.TotalPlays.ToString("N0"));
        Row("Unique tracks", r.UniqueTracks.ToString("N0"));
        Row("Unique artists", r.UniqueArtists.ToString("N0"));
        Row("Unique albums", r.UniqueAlbums.ToString("N0"));
        if (r.FirstListen is { } first && r.LastListen is { } last)
            Row("Date range", $"{TimeFormat.Timestamp(first)} to {TimeFormat.Timestamp(last)}");
        if (r.ActiveDays > 0)
            Row("Active days", r.ActiveDays.ToString("N0"));
        sb.AppendLine();

        sb.AppendLine("## Insights").AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| --- | --- |");
        if (r.LongestStreakDays > 0 && r.LongestStreakStart is { } ss && r.LongestStreakEnd is { } se)
            Row("Longest streak", $"{r.LongestStreakDays} days ({ss:yyyy-MM-dd} to {se:yyyy-MM-dd})");
        if (r.CurrentStreakDays > 0)
            Row("Current streak", $"{r.CurrentStreakDays} days");
        if (r.LongestBreakDays > 0 && r.LongestBreakStart is { } bs && r.LongestBreakEnd is { } be)
            Row("Longest break", $"{r.LongestBreakDays} days ({bs:yyyy-MM-dd} to {be:yyyy-MM-dd})");
        if (r.BiggestDay is { } bd)
            Row("Biggest day", $"{bd:yyyy-MM-dd} ({TimeFormat.Friendly(TimeSpan.FromMilliseconds(r.BiggestDayMs))})");
        if (r.SessionCount > 0)
        {
            Row("Listening sessions", r.SessionCount.ToString("N0"));
            Row("Average session", TimeFormat.Friendly(TimeSpan.FromMilliseconds(r.AvgSessionMs)));
            Row("Longest session", TimeFormat.Friendly(TimeSpan.FromMilliseconds(r.LongestSessionMs)));
        }
        if (r.SkipEligiblePlays > 0)
        {
            Row("Skip rate", $"{r.TotalSkips * 100.0 / r.SkipEligiblePlays:0.#}%");
            Row("Completion rate", $"{r.CompletedPlays * 100.0 / r.SkipEligiblePlays:0.#}%");
        }
        if (r.UniqueTracks > 0)
            Row("Plays per track", r.PlaysPerTrack.ToString("0.0"));
        if (r.Artists.Count > 0)
            Row("Top artist share", $"{r.Artists[0].Artist} ({r.TopArtistSharePercent:0.#}%)");
        if (r.ShuffleEligiblePlays > 0)
            Row("Shuffle rate", $"{r.ShufflePlays * 100.0 / r.ShuffleEligiblePlays:0.#}%");
        sb.AppendLine();

        Table("Top tracks", "Rank | Track | Artist | Time | Plays", "--- | --- | --- | --- | ---",
            r.Tracks.Take(MarkdownTopN).Select((t, i) =>
                $"{i + 1} | {Md(t.Track)} | {Md(t.Artist)} | {TimeFormat.HhMmSs(t.TotalTime)} | {t.PlayCount:N0}"));

        Table("Top artists", "Rank | Artist | Time | Plays", "--- | --- | --- | ---",
            r.Artists.Take(MarkdownTopN).Select((a, i) =>
                $"{i + 1} | {Md(a.Artist)} | {TimeFormat.HhMmSs(a.TotalTime)} | {a.PlayCount:N0}"));

        Table("Top albums", "Rank | Album | Artist | Time | Plays", "--- | --- | --- | --- | ---",
            r.Albums.Take(MarkdownTopN).Select((a, i) =>
                $"{i + 1} | {Md(a.Album)} | {Md(a.Artist)} | {TimeFormat.HhMmSs(a.TotalTime)} | {a.PlayCount:N0}"));

        if (r.Years.Count > 0)
            Table("By year", "Year | Time | Plays | Artists | Tracks | Top artist | Top track",
                "--- | --- | --- | --- | --- | --- | ---",
                r.Years.Select(y =>
                    $"{y.Year} | {TimeFormat.HhMmSs(y.TotalTime)} | {y.PlayCount:N0} | {y.UniqueArtists:N0} | " +
                    $"{y.UniqueTracks:N0} | {Md(y.TopArtist)} | {Md(y.TopTrack)}"));

        if (r.Shows.Count > 0)
            Table("Top shows and audiobooks", "Rank | Show | Time | Plays | Episodes",
                "--- | --- | --- | --- | ---",
                r.Shows.Take(MarkdownTopN).Select((s, i) =>
                    $"{i + 1} | {Md(s.Show)} | {TimeFormat.HhMmSs(s.TotalTime)} | {s.PlayCount:N0} | {s.EpisodeCount:N0}"));

        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);

        void Row(string name, string value) => sb.AppendLine($"| {name} | {value} |");

        void Table(string heading, string header, string divider, IEnumerable<string> rows)
        {
            sb.AppendLine($"## {heading}").AppendLine();
            sb.AppendLine($"| {header} |");
            sb.AppendLine($"| {divider} |");
            foreach (var row in rows)
                sb.AppendLine($"| {row} |");
            sb.AppendLine();
        }
    }

    /// <summary>Escapes the pipe that would otherwise split a Markdown table cell.</summary>
    internal static string Md(string value) => value.Replace("|", "\\|");

    /// <summary>
    /// Escapes what Markdown would read as formatting in running text. A search term or an
    /// excluded artist's name can contain anything.
    /// </summary>
    internal static string MdText(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (c is '\\' or '*' or '_' or '`' or '[' or ']' or '<' or '>' or '#' or '|')
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    // ---- JSON ----------------------------------------------------------------------------

    /// <summary>
    /// The whole result as machine-readable JSON, for anyone who wants to do their own
    /// analysis on top. Shaped explicitly rather than serializing
    /// <see cref="AnalysisResult"/> directly, so the file format is stable across refactors.
    /// </summary>
    public static async Task SaveJsonAsync(string path, AnalysisResult r, IReadOnlyList<string>? filters = null)
    {
        var payload = new
        {
            generated = DateTime.Now,
            filters = filters ?? Array.Empty<string>(),
            summary = new
            {
                totalTime = TimeFormat.HhMmSs(r.TotalTime),
                totalHours = Math.Round(r.TotalMsPlayed / 3_600_000d, 2),
                totalPlays = r.TotalPlays,
                uniqueTracks = r.UniqueTracks,
                uniqueArtists = r.UniqueArtists,
                uniqueAlbums = r.UniqueAlbums,
                firstListen = r.FirstListen,
                lastListen = r.LastListen,
                activeDays = r.ActiveDays,
            },
            insights = new
            {
                longestStreakDays = r.LongestStreakDays,
                longestStreakStart = r.LongestStreakStart,
                longestStreakEnd = r.LongestStreakEnd,
                currentStreakDays = r.CurrentStreakDays,
                longestBreakDays = r.LongestBreakDays,
                longestBreakStart = r.LongestBreakStart,
                longestBreakEnd = r.LongestBreakEnd,
                biggestDay = r.BiggestDay,
                biggestDayHours = Math.Round(r.BiggestDayMs / 3_600_000d, 2),
                sessionCount = r.SessionCount,
                avgSessionMinutes = Math.Round(r.AvgSessionMs / 60_000d, 1),
                longestSessionMinutes = Math.Round(r.LongestSessionMs / 60_000d, 1),
                skipEligiblePlays = r.SkipEligiblePlays,
                skips = r.TotalSkips,
                completedPlays = r.CompletedPlays,
                playsPerTrack = Math.Round(r.PlaysPerTrack, 2),
                newArtistsPerMonth = Math.Round(r.NewArtistsPerMonth, 2),
                topArtistSharePercent = Math.Round(r.TopArtistSharePercent, 2),
                shufflePlays = r.ShufflePlays,
                shuffleEligiblePlays = r.ShuffleEligiblePlays,
                offlinePlays = r.OfflinePlays,
                incognitoPlays = r.IncognitoPlays,
            },
            tracks = r.Tracks.Select(t => new
            {
                track = t.Track,
                artist = t.Artist,
                hours = Math.Round(t.TotalHours, 4),
                plays = t.PlayCount,
                firstPlayed = t.FirstPlayed,
                lastPlayed = t.LastPlayed,
            }),
            artists = r.Artists.Select(a => new
            {
                artist = a.Artist,
                hours = Math.Round(a.TotalHours, 4),
                plays = a.PlayCount,
                firstPlayed = a.FirstPlayed,
                lastPlayed = a.LastPlayed,
                firstTrack = a.FirstTrack,
            }),
            albums = r.Albums.Select(a => new
            {
                album = a.Album,
                artist = a.Artist,
                hours = Math.Round(a.TotalHours, 4),
                plays = a.PlayCount,
                firstPlayed = a.FirstPlayed,
                lastPlayed = a.LastPlayed,
            }),
            years = r.Years.Select(y => new
            {
                year = y.Year,
                hours = Math.Round(y.TotalHours, 4),
                plays = y.PlayCount,
                uniqueArtists = y.UniqueArtists,
                uniqueTracks = y.UniqueTracks,
                topArtist = y.TopArtist,
                topTrack = y.TopTrack,
            }),
            shows = r.Shows.Select(s => new
            {
                show = s.Show,
                kind = s.Kind.ToString(),
                hours = Math.Round(s.TotalHours, 4),
                plays = s.PlayCount,
                episodes = s.EpisodeCount,
            }),
            episodes = r.Episodes.Select(e => new
            {
                episode = e.Episode,
                show = e.Show,
                hours = Math.Round(e.TotalHours, 4),
                plays = e.PlayCount,
            }),
            hoursByHourOfDay = r.PlaytimeByHour.Select(ms => Math.Round(ms / 3_600_000d, 4)),
            hoursByDayOfWeek = r.PlaytimeByDayOfWeek.Select(ms => Math.Round(ms / 3_600_000d, 4)),
            platforms = r.Platforms.Select(p => new { name = p.Name, hours = Math.Round(p.TotalHours, 4), plays = p.PlayCount }),
            countries = r.Countries.Select(c => new { name = c.Name, hours = Math.Round(c.TotalHours, 4), plays = c.PlayCount }),
            reasonEnds = r.ReasonEnds.Select(x => new { reason = x.Reason, count = x.Count }),
        };

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            64 * 1024, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions).ConfigureAwait(false);
    }

    /// <summary>Quotes a CSV field when it contains a delimiter, quote or line break.</summary>
    internal static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }
}
