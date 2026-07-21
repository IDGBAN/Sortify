using System.IO;
using System.Text;
using Sortify.Models;

namespace Sortify.Services;

/// <summary>Exports analysis results to TXT (parity with the original app) and CSV.</summary>
public static class ExportService
{
    public static async Task SaveTxtAsync(string path, AnalysisResult result)
    {
        var sb = new StringBuilder();

        // ---- Summary -------------------------------------------------------------------
        sb.AppendLine("Sortify Results");
        sb.AppendLine($"Generated: {TimeFormat.Timestamp(DateTime.Now)}");
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
        if (result.BiggestDay is { } bd)
            sb.AppendLine($"Biggest day: {bd:yyyy-MM-dd} ({TimeFormat.Friendly(TimeSpan.FromMilliseconds(result.BiggestDayMs))})");
        if (result.SessionCount > 0)
            sb.AppendLine($"Listening sessions: {result.SessionCount:N0} (avg {TimeFormat.Friendly(TimeSpan.FromMilliseconds(result.AvgSessionMs))})");
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
              .Append(t.TotalHours.ToString("0.00")).Append(',')
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
              .Append(a.TotalHours.ToString("0.00")).Append(',')
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
              .Append(a.TotalHours.ToString("0.00")).Append(',')
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
              .Append(y.TotalHours.ToString("0.00")).Append(',')
              .Append(y.PlayCount).Append(',')
              .Append(y.UniqueArtists).Append(',')
              .Append(y.UniqueTracks).Append(',')
              .Append(Csv(y.TopArtist)).Append(',')
              .Append(Csv(y.TopTrack))
              .AppendLine();
        }
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);
    }

    /// <summary>Quotes a CSV field when it contains a delimiter, quote or line break.</summary>
    internal static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }
}
