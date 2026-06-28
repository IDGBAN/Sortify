using Sortify.Models;

namespace Sortify.Services;

/// <summary>
/// Aggregates filtered play records into per-artist and per-track statistics plus
/// time-based breakdowns used by the charts. Replaces the original Python Counter logic.
/// </summary>
public static class AnalysisEngine
{
    /// <summary>Runs aggregation on a background thread so the UI stays responsive.</summary>
    public static Task<AnalysisResult> AnalyzeAsync(
        IReadOnlyList<PlayRecord> records,
        FilterOptions filter,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Analyze(records, filter, cancellationToken), cancellationToken);
    }

    public static AnalysisResult Analyze(
        IReadOnlyList<PlayRecord> records,
        FilterOptions filter,
        CancellationToken cancellationToken = default)
    {
        var artists = new Dictionary<string, ArtistStat>(StringComparer.Ordinal);
        var tracks = new Dictionary<string, TrackStat>(StringComparer.Ordinal);
        var byDay = new Dictionary<DateTime, long>();
        var byHour = new long[24];
        var byDow = new long[7];

        int totalPlays = 0;
        long totalMs = 0;
        DateTime firstListen = DateTime.MaxValue;
        DateTime lastListen = DateTime.MinValue;

        int counter = 0;
        foreach (var r in FilterEngine.Apply(records, filter))
        {
            if ((++counter & 0x3FFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            totalPlays++;
            totalMs += r.MsPlayed;

            // Artist aggregate.
            if (!artists.TryGetValue(r.ArtistName, out var artist))
            {
                artist = new ArtistStat { Artist = r.ArtistName };
                artists[r.ArtistName] = artist;
            }
            artist.TotalMsPlayed += r.MsPlayed;
            artist.PlayCount++;

            // Track aggregate keyed by "track\u0000artist" to avoid collisions across artists.
            var trackKey = r.TrackName + "\u0000" + r.ArtistName;
            if (!tracks.TryGetValue(trackKey, out var track))
            {
                track = new TrackStat { Track = r.TrackName, Artist = r.ArtistName };
                tracks[trackKey] = track;
            }
            track.TotalMsPlayed += r.MsPlayed;
            track.PlayCount++;

            if (r.Timestamp != DateTime.MinValue)
            {
                if (r.Timestamp < artist.FirstPlayed)
                {
                    artist.FirstPlayed = r.Timestamp;
                    artist.FirstTrack = r.TrackName;
                }
                if (r.Timestamp > artist.LastPlayed) artist.LastPlayed = r.Timestamp;

                if (r.Timestamp < track.FirstPlayed) track.FirstPlayed = r.Timestamp;
                if (r.Timestamp > track.LastPlayed) track.LastPlayed = r.Timestamp;

                if (r.Timestamp < firstListen) firstListen = r.Timestamp;
                if (r.Timestamp > lastListen) lastListen = r.Timestamp;

                var day = r.Timestamp.Date;
                byDay[day] = byDay.TryGetValue(day, out var d) ? d + r.MsPlayed : r.MsPlayed;
                byHour[r.Timestamp.Hour] += r.MsPlayed;
                byDow[(int)r.Timestamp.DayOfWeek] += r.MsPlayed;
            }
        }

        var artistList = artists.Values
            .OrderByDescending(a => a.TotalMsPlayed)
            .ToList();
        var trackList = tracks.Values
            .OrderByDescending(t => t.TotalMsPlayed)
            .ToList();

        var dayPoints = byDay
            .OrderBy(kv => kv.Key)
            .Select(kv => new DateTimePoint(kv.Key, kv.Value / 3_600_000d))
            .ToList();

        return new AnalysisResult
        {
            Artists = artistList,
            Tracks = trackList,
            TotalPlays = totalPlays,
            TotalMsPlayed = totalMs,
            FirstListen = firstListen == DateTime.MaxValue ? null : firstListen,
            LastListen = lastListen == DateTime.MinValue ? null : lastListen,
            PlaytimeByDay = dayPoints,
            PlaytimeByHour = byHour,
            PlaytimeByDayOfWeek = byDow,
        };
    }
}
