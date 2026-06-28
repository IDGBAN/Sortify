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

        sb.AppendLine("Tracks Ranked by Time Listened:");
        int rank = 1;
        foreach (var t in result.Tracks.OrderByDescending(t => t.TotalMsPlayed))
            sb.AppendLine($"{rank++}. {TimeFormat.HhMmSs(t.TotalTime)} - {t.Track} - {t.Artist}");
        sb.AppendLine();

        sb.AppendLine("Tracks Ranked by Counts Listened:");
        rank = 1;
        foreach (var t in result.Tracks.OrderByDescending(t => t.PlayCount))
            sb.AppendLine($"{rank++}. {t.PlayCount} times - {t.Track} - {t.Artist}");
        sb.AppendLine();

        sb.AppendLine("Tracks Ranked by First Time Listened:");
        rank = 1;
        foreach (var t in result.Tracks
                     .Where(t => t.FirstPlayed != DateTime.MaxValue)
                     .OrderBy(t => t.FirstPlayed))
            sb.AppendLine($"{rank++}. {TimeFormat.Timestamp(t.FirstPlayed)} - {t.Track} - {t.Artist}");
        sb.AppendLine();

        sb.AppendLine("Artists Ranked by Time Listened:");
        rank = 1;
        foreach (var a in result.Artists.OrderByDescending(a => a.TotalMsPlayed))
            sb.AppendLine($"{rank++}. {TimeFormat.HhMmSs(a.TotalTime)} - {a.Artist}");
        sb.AppendLine();

        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);
    }

    public static async Task SaveTracksCsvAsync(string path, AnalysisResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Rank,Track,Artist,TotalTime,Hours,PlayCount,FirstPlayed,LastPlayed");
        int rank = 1;
        foreach (var t in result.Tracks.OrderByDescending(t => t.TotalMsPlayed))
        {
            sb.Append(rank++).Append(',')
              .Append(Csv(t.Track)).Append(',')
              .Append(Csv(t.Artist)).Append(',')
              .Append(TimeFormat.HhMmSs(t.TotalTime)).Append(',')
              .Append(t.TotalHours.ToString("0.00")).Append(',')
              .Append(t.PlayCount).Append(',')
              .Append(t.FirstPlayed == DateTime.MaxValue ? "" : TimeFormat.Timestamp(t.FirstPlayed)).Append(',')
              .Append(t.LastPlayed == DateTime.MinValue ? "" : TimeFormat.Timestamp(t.LastPlayed))
              .AppendLine();
        }
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);
    }

    public static async Task SaveArtistsCsvAsync(string path, AnalysisResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Rank,Artist,TotalTime,Hours,PlayCount,FirstPlayed,FirstTrack,LastPlayed");
        int rank = 1;
        foreach (var a in result.Artists.OrderByDescending(a => a.TotalMsPlayed))
        {
            sb.Append(rank++).Append(',')
              .Append(Csv(a.Artist)).Append(',')
              .Append(TimeFormat.HhMmSs(a.TotalTime)).Append(',')
              .Append(a.TotalHours.ToString("0.00")).Append(',')
              .Append(a.PlayCount).Append(',')
              .Append(a.FirstPlayed == DateTime.MaxValue ? "" : TimeFormat.Timestamp(a.FirstPlayed)).Append(',')
              .Append(Csv(a.FirstTrack)).Append(',')
              .Append(a.LastPlayed == DateTime.MinValue ? "" : TimeFormat.Timestamp(a.LastPlayed))
              .AppendLine();
        }
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);
    }

    private static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }
}
