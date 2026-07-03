using Sortify.Models;

namespace Sortify.Services;

/// <summary>
/// Aggregates filtered play records into per-artist, per-track and per-album statistics
/// plus time-based breakdowns, skip statistics and streaks used by the charts and insights.
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
        var albums = new Dictionary<string, AlbumStat>(StringComparer.Ordinal);
        var skips = new Dictionary<string, SkippedTrackStat>(StringComparer.Ordinal);
        var byDay = new Dictionary<DateTime, long>();
        var byHour = new long[24];
        var byDow = new long[7];
        var byDowHour = new long[7, 24];

        int totalPlays = 0;
        long totalMs = 0;
        int skipEligiblePlays = 0;
        int totalSkips = 0;
        DateTime firstListen = DateTime.MaxValue;
        DateTime lastListen = DateTime.MinValue;

        // Enumerate with the min-duration cutoff relaxed so skip statistics see the short
        // plays that skipping produces; the cutoff is re-applied manually for everything else.
        int counter = 0;
        foreach (var r in FilterEngine.Apply(records, filter, ignoreMinDuration: true))
        {
            if ((++counter & 0x3FFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var trackKey = r.TrackName + "\u0000" + r.ArtistName;

            skipEligiblePlays++;
            if (r.Skipped)
                totalSkips++;
            if (!skips.TryGetValue(trackKey, out var skip))
            {
                skip = new SkippedTrackStat { Track = r.TrackName, Artist = r.ArtistName };
                skips[trackKey] = skip;
            }
            skip.PlayCount++;
            if (r.Skipped) skip.SkipCount++;

            if (r.MsPlayed < filter.MinMsPlayed)
                continue;

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
            if (!tracks.TryGetValue(trackKey, out var track))
            {
                track = new TrackStat { Track = r.TrackName, Artist = r.ArtistName };
                tracks[trackKey] = track;
            }
            track.TotalMsPlayed += r.MsPlayed;
            track.PlayCount++;

            // Album aggregate keyed the same way.
            var albumKey = r.AlbumName + "\u0000" + r.ArtistName;
            if (!albums.TryGetValue(albumKey, out var album))
            {
                album = new AlbumStat { Album = r.AlbumName, Artist = r.ArtistName };
                albums[albumKey] = album;
            }
            album.TotalMsPlayed += r.MsPlayed;
            album.PlayCount++;

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

                if (r.Timestamp < album.FirstPlayed) album.FirstPlayed = r.Timestamp;
                if (r.Timestamp > album.LastPlayed) album.LastPlayed = r.Timestamp;

                if (r.Timestamp < firstListen) firstListen = r.Timestamp;
                if (r.Timestamp > lastListen) lastListen = r.Timestamp;

                var day = r.Timestamp.Date;
                byDay[day] = byDay.TryGetValue(day, out var d) ? d + r.MsPlayed : r.MsPlayed;
                byHour[r.Timestamp.Hour] += r.MsPlayed;
                byDow[(int)r.Timestamp.DayOfWeek] += r.MsPlayed;
                byDowHour[(int)r.Timestamp.DayOfWeek, r.Timestamp.Hour] += r.MsPlayed;
            }
        }

        var artistList = artists.Values
            .OrderByDescending(a => a.TotalMsPlayed)
            .ToList();
        var trackList = tracks.Values
            .OrderByDescending(t => t.TotalMsPlayed)
            .ToList();
        var albumList = albums.Values
            .OrderByDescending(a => a.TotalMsPlayed)
            .ToList();
        var skippedList = skips.Values
            .Where(s => s.SkipCount > 0)
            .OrderByDescending(s => s.SkipCount)
            .ToList();

        var dayPoints = byDay
            .OrderBy(kv => kv.Key)
            .Select(kv => new DateTimePoint(kv.Key, kv.Value / 3_600_000d))
            .ToList();

        var (streakDays, streakStart, streakEnd) = LongestStreak(dayPoints);

        DateTime? biggestDay = null;
        long biggestDayMs = 0;
        foreach (var (day, ms) in byDay)
        {
            if (ms > biggestDayMs)
            {
                biggestDayMs = ms;
                biggestDay = day;
            }
        }

        return new AnalysisResult
        {
            Artists = artistList,
            Tracks = trackList,
            Albums = albumList,
            SkippedTracks = skippedList,
            TotalPlays = totalPlays,
            TotalMsPlayed = totalMs,
            SkipEligiblePlays = skipEligiblePlays,
            TotalSkips = totalSkips,
            FirstListen = firstListen == DateTime.MaxValue ? null : firstListen,
            LastListen = lastListen == DateTime.MinValue ? null : lastListen,
            PlaytimeByDay = dayPoints,
            PlaytimeByHour = byHour,
            PlaytimeByDayOfWeek = byDow,
            PlaytimeByDowHour = byDowHour,
            ActiveDays = byDay.Count,
            LongestStreakDays = streakDays,
            LongestStreakStart = streakStart,
            LongestStreakEnd = streakEnd,
            BiggestDay = biggestDay,
            BiggestDayMs = biggestDayMs,
        };
    }

    /// <summary>Finds the longest run of consecutive listening days in date-sorted day points.</summary>
    private static (int days, DateTime? start, DateTime? end) LongestStreak(IReadOnlyList<DateTimePoint> dayPoints)
    {
        if (dayPoints.Count == 0)
            return (0, null, null);

        int best = 1, current = 1;
        DateTime bestStart = dayPoints[0].Date, bestEnd = dayPoints[0].Date;
        DateTime runStart = dayPoints[0].Date;

        for (int i = 1; i < dayPoints.Count; i++)
        {
            if (dayPoints[i].Date == dayPoints[i - 1].Date.AddDays(1))
            {
                current++;
            }
            else
            {
                current = 1;
                runStart = dayPoints[i].Date;
            }

            if (current > best)
            {
                best = current;
                bestStart = runStart;
                bestEnd = dayPoints[i].Date;
            }
        }

        return (best, bestStart, bestEnd);
    }
}
