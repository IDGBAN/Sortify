using System.Runtime.InteropServices;
using Sortify.Models;

namespace Sortify.Services;

/// <summary>
/// Aggregates filtered play records into per-artist, per-track, per-album and per-year
/// statistics plus time-based breakdowns, skip statistics, sessions and streaks used by
/// the charts and insights.
/// </summary>
public static class AnalysisEngine
{
    /// <summary>Plays separated by more than this gap belong to different listening sessions.</summary>
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);

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
        var skips = new Dictionary<string, (int Plays, int Skips)>(StringComparer.Ordinal);
        var reasons = new Dictionary<string, ReasonEndStat>(StringComparer.OrdinalIgnoreCase);
        var years = new Dictionary<int, YearAccumulator>();
        var byDay = new Dictionary<DateTime, long>();
        var byHour = new long[24];
        var byDow = new long[7];
        var byDowHour = new long[7, 24];
        var timedPlays = new List<(DateTime End, int Ms)>();

        var shows = new Dictionary<string, ShowStat>(StringComparer.Ordinal);
        var showEpisodeKeys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var episodes = new Dictionary<string, EpisodeStat>(StringComparer.Ordinal);
        var platforms = new Dictionary<string, ContextStat>(StringComparer.Ordinal);
        var countries = new Dictionary<string, ContextStat>(StringComparer.OrdinalIgnoreCase);

        int totalPlays = 0;
        long totalMs = 0;
        int skipEligiblePlays = 0;
        int totalSkips = 0;
        long podcastMs = 0;
        int podcastPlays = 0;
        int shufflePlays = 0;
        int shuffleEligible = 0;
        int offlinePlays = 0;
        int incognitoPlays = 0;
        DateTime? firstListen = null;
        DateTime? lastListen = null;

        // Enumerate with the min-duration cutoff relaxed so skip and reason-end statistics
        // see the short plays that skipping produces; the cutoff is re-applied manually
        // for everything else.
        int counter = 0;
        foreach (var r in FilterEngine.Apply(records, filter, ignoreMinDuration: true))
        {
            if ((++counter & 0x3FFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            bool isMusic = r.Kind == ContentKind.Music;

            // ---- Podcast / audiobook aggregates ---------------------------------------------
            // Always collected so the Podcasts tab works regardless of IncludePodcasts, which
            // only governs whether this content also feeds the music statistics below.
            if (!isMusic && r.MsPlayed >= filter.MinMsPlayed)
            {
                podcastPlays++;
                podcastMs += r.MsPlayed;
                AccumulateShow(shows, showEpisodeKeys, episodes, r);
            }

            // ---- Playback context ------------------------------------------------------------
            // Describes all listening, so it is collected before the music-only gate.
            if (r.MsPlayed >= filter.MinMsPlayed)
            {
                if (r.Platform.Length > 0)
                    Bump(platforms, PlatformFamily(r.Platform), r.MsPlayed);
                if (r.Country.Length > 0)
                    Bump(countries, r.Country, r.MsPlayed);

                // Older exports omit these flags entirely; only count rows that carry one so
                // the percentages aren't diluted by records that never could have set it.
                if (r.HasPlaybackFlags)
                {
                    shuffleEligible++;
                    if (r.Shuffle) shufflePlays++;
                    if (r.Offline) offlinePlays++;
                    if (r.Incognito) incognitoPlays++;
                }
            }

            // Podcasts and audiobooks stay out of the music statistics unless asked for.
            if (!isMusic && !filter.IncludePodcasts)
                continue;

            // When podcasts are folded in, the episode stands in for the track and the show
            // for both the artist and the album, so every ranking stays populated.
            string trackName = isMusic ? r.TrackName : r.EpisodeName;
            string artistName = isMusic ? r.ArtistName : r.ShowName;
            string albumName = isMusic ? r.AlbumName : r.ShowName;

            var trackKey = trackName + "\n" + artistName;

            // ---- Relaxed-duration statistics: skips and end reasons -------------------------
            skipEligiblePlays++;
            if (r.Skipped)
                totalSkips++;

            // Counted as a plain value tuple: allocating a SkippedTrackStat per unique track
            // wastes one object for every track that was never skipped, which is most of them.
            ref var counts = ref CollectionsMarshal.GetValueRefOrAddDefault(skips, trackKey, out _);
            counts.Plays++;
            if (r.Skipped) counts.Skips++;

            var reasonKey = string.IsNullOrWhiteSpace(r.ReasonEnd) ? "unknown" : r.ReasonEnd;
            if (!reasons.TryGetValue(reasonKey, out var reason))
            {
                reason = new ReasonEndStat { Reason = reasonKey };
                reasons[reasonKey] = reason;
            }
            reason.Count++;

            // ---- Everything below only counts plays that pass the duration cutoff ----------
            if (r.MsPlayed < filter.MinMsPlayed)
                continue;

            totalPlays++;
            totalMs += r.MsPlayed;

            // Artist aggregate.
            if (!artists.TryGetValue(artistName, out var artist))
            {
                artist = new ArtistStat { Artist = artistName };
                artists[artistName] = artist;
            }
            artist.TotalMsPlayed += r.MsPlayed;
            artist.PlayCount++;

            // Track aggregate keyed by "track\nartist" to avoid collisions across artists.
            if (!tracks.TryGetValue(trackKey, out var track))
            {
                track = new TrackStat { Track = trackName, Artist = artistName };
                tracks[trackKey] = track;
            }
            track.TotalMsPlayed += r.MsPlayed;
            track.PlayCount++;

            // Album aggregate keyed the same way.
            var albumKey = albumName + "\n" + artistName;
            if (!albums.TryGetValue(albumKey, out var album))
            {
                album = new AlbumStat { Album = albumName, Artist = artistName };
                albums[albumKey] = album;
            }
            album.TotalMsPlayed += r.MsPlayed;
            album.PlayCount++;

            if (r.Timestamp != DateTime.MinValue)
            {
                if (artist.FirstPlayed is null || r.Timestamp < artist.FirstPlayed)
                {
                    artist.FirstPlayed = r.Timestamp;
                    artist.FirstTrack = trackName;
                }
                if (artist.LastPlayed is null || r.Timestamp > artist.LastPlayed) artist.LastPlayed = r.Timestamp;

                if (track.FirstPlayed is null || r.Timestamp < track.FirstPlayed) track.FirstPlayed = r.Timestamp;
                if (track.LastPlayed is null || r.Timestamp > track.LastPlayed) track.LastPlayed = r.Timestamp;

                if (album.FirstPlayed is null || r.Timestamp < album.FirstPlayed) album.FirstPlayed = r.Timestamp;
                if (album.LastPlayed is null || r.Timestamp > album.LastPlayed) album.LastPlayed = r.Timestamp;

                if (firstListen is null || r.Timestamp < firstListen) firstListen = r.Timestamp;
                if (lastListen is null || r.Timestamp > lastListen) lastListen = r.Timestamp;

                var day = r.Timestamp.Date;
                byDay[day] = byDay.TryGetValue(day, out var d) ? d + r.MsPlayed : r.MsPlayed;
                byHour[r.Timestamp.Hour] += r.MsPlayed;
                byDow[(int)r.Timestamp.DayOfWeek] += r.MsPlayed;
                byDowHour[(int)r.Timestamp.DayOfWeek, r.Timestamp.Hour] += r.MsPlayed;

                timedPlays.Add((r.Timestamp, r.MsPlayed));

                // Per-year rollup.
                int year = r.Timestamp.Year;
                if (!years.TryGetValue(year, out var acc))
                {
                    acc = new YearAccumulator();
                    years[year] = acc;
                }
                acc.Ms += r.MsPlayed;
                acc.Plays++;
                acc.ArtistMs[artistName] = acc.ArtistMs.TryGetValue(artistName, out var am) ? am + r.MsPlayed : r.MsPlayed;
                acc.TrackMs[trackKey] = acc.TrackMs.TryGetValue(trackKey, out var tm) ? tm + r.MsPlayed : r.MsPlayed;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var artistList = artists.Values.OrderByDescending(a => a.TotalMsPlayed).ToList();
        var trackList = tracks.Values.OrderByDescending(t => t.TotalMsPlayed).ToList();
        var albumList = albums.Values.OrderByDescending(a => a.TotalMsPlayed).ToList();
        var artistsByCount = artists.Values.OrderByDescending(a => a.PlayCount).ToList();
        var tracksByCount = tracks.Values.OrderByDescending(t => t.PlayCount).ToList();
        var albumsByCount = albums.Values.OrderByDescending(a => a.PlayCount).ToList();
        // Only tracks that were actually skipped become objects; the rest never leave the
        // counting dictionary.
        var skippedList = new List<SkippedTrackStat>();
        foreach (var (key, counts) in skips)
        {
            if (counts.Skips == 0)
                continue;
            int sep = key.IndexOf('\n');
            skippedList.Add(new SkippedTrackStat
            {
                Track = key[..sep],
                Artist = key[(sep + 1)..],
                SkipCount = counts.Skips,
                PlayCount = counts.Plays,
            });
        }
        skippedList.Sort(static (a, b) => b.SkipCount.CompareTo(a.SkipCount));

        var reasonList = reasons.Values.OrderByDescending(x => x.Count).ToList();

        var showList = shows.Values.OrderByDescending(s => s.TotalMsPlayed).ToList();
        var episodeList = episodes.Values.OrderByDescending(e => e.TotalMsPlayed).ToList();
        var platformList = platforms.Values.OrderByDescending(p => p.TotalMsPlayed).ToList();
        var countryList = countries.Values.OrderByDescending(c => c.TotalMsPlayed).ToList();

        var dayPoints = byDay
            .OrderBy(kv => kv.Key)
            .Select(kv => new DateTimePoint(kv.Key, kv.Value / 3_600_000d))
            .ToList();

        var (streakDays, streakStart, streakEnd) = LongestStreak(dayPoints);
        int currentStreak = CurrentStreak(dayPoints);

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

        var (sessionCount, avgSessionMs, longestSessionMs, longestSessionDate) = ComputeSessions(timedPlays);

        var newArtistsByMonth = artistList
            .Where(a => a.FirstPlayed is not null)
            .GroupBy(a => new DateTime(a.FirstPlayed!.Value.Year, a.FirstPlayed.Value.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => new DateTimePoint(g.Key, g.Count()))
            .ToList();

        var yearList = years
            .OrderBy(kv => kv.Key)
            .Select(kv => BuildYearStat(kv.Key, kv.Value))
            .ToList();

        return new AnalysisResult
        {
            Artists = artistList,
            Tracks = trackList,
            Albums = albumList,
            ArtistsByPlayCount = artistsByCount,
            TracksByPlayCount = tracksByCount,
            AlbumsByPlayCount = albumsByCount,
            Years = yearList,
            ReasonEnds = reasonList,
            SkippedTracks = skippedList,
            Shows = showList,
            Episodes = episodeList,
            PodcastMsPlayed = podcastMs,
            PodcastPlays = podcastPlays,
            Platforms = platformList,
            Countries = countryList,
            ShufflePlays = shufflePlays,
            ShuffleEligiblePlays = shuffleEligible,
            OfflinePlays = offlinePlays,
            IncognitoPlays = incognitoPlays,
            TotalPlays = totalPlays,
            TotalMsPlayed = totalMs,
            SkipEligiblePlays = skipEligiblePlays,
            TotalSkips = totalSkips,
            FirstListen = firstListen,
            LastListen = lastListen,
            PlaytimeByDay = dayPoints,
            PlaytimeByHour = byHour,
            PlaytimeByDayOfWeek = byDow,
            PlaytimeByDowHour = byDowHour,
            NewArtistsByMonth = newArtistsByMonth,
            ActiveDays = byDay.Count,
            LongestStreakDays = streakDays,
            LongestStreakStart = streakStart,
            LongestStreakEnd = streakEnd,
            CurrentStreakDays = currentStreak,
            BiggestDay = biggestDay,
            BiggestDayMs = biggestDayMs,
            SessionCount = sessionCount,
            AvgSessionMs = avgSessionMs,
            LongestSessionMs = longestSessionMs,
            LongestSessionDate = longestSessionDate,
        };
    }

    /// <summary>Adds one podcast/audiobook play to the show and episode aggregates.</summary>
    private static void AccumulateShow(
        Dictionary<string, ShowStat> shows,
        Dictionary<string, HashSet<string>> showEpisodeKeys,
        Dictionary<string, EpisodeStat> episodes,
        PlayRecord r)
    {
        if (!shows.TryGetValue(r.ShowName, out var show))
        {
            show = new ShowStat { Show = r.ShowName, Kind = r.Kind };
            shows[r.ShowName] = show;
            showEpisodeKeys[r.ShowName] = new HashSet<string>(StringComparer.Ordinal);
        }
        show.TotalMsPlayed += r.MsPlayed;
        show.PlayCount++;
        if (showEpisodeKeys[r.ShowName].Add(r.EpisodeName))
            show.EpisodeCount++;

        var episodeKey = r.EpisodeName + "\n" + r.ShowName;
        if (!episodes.TryGetValue(episodeKey, out var episode))
        {
            episode = new EpisodeStat { Episode = r.EpisodeName, Show = r.ShowName };
            episodes[episodeKey] = episode;
        }
        episode.TotalMsPlayed += r.MsPlayed;
        episode.PlayCount++;

        if (r.Timestamp == DateTime.MinValue)
            return;

        if (show.FirstPlayed is null || r.Timestamp < show.FirstPlayed) show.FirstPlayed = r.Timestamp;
        if (show.LastPlayed is null || r.Timestamp > show.LastPlayed) show.LastPlayed = r.Timestamp;
        if (episode.FirstPlayed is null || r.Timestamp < episode.FirstPlayed) episode.FirstPlayed = r.Timestamp;
        if (episode.LastPlayed is null || r.Timestamp > episode.LastPlayed) episode.LastPlayed = r.Timestamp;
    }

    private static void Bump(Dictionary<string, ContextStat> map, string name, int ms)
    {
        if (!map.TryGetValue(name, out var stat))
        {
            stat = new ContextStat { Name = name };
            map[name] = stat;
        }
        stat.TotalMsPlayed += ms;
        stat.PlayCount++;
    }

    /// <summary>
    /// Collapses Spotify's very granular platform strings (which embed OS builds, device
    /// models and SDK versions) into a handful of families worth charting.
    /// </summary>
    internal static string PlatformFamily(string platform)
    {
        if (platform.Contains("web_player", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("webplayer", StringComparison.OrdinalIgnoreCase))
            return "Web player";
        if (platform.Contains("android", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("ios", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("iphone", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("ipad", StringComparison.OrdinalIgnoreCase))
            return "Mobile";
        if (platform.Contains("cast", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("sonos", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("speaker", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("partner", StringComparison.OrdinalIgnoreCase))
            return "Speaker / cast";
        if (platform.Contains("tv", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("xbox", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("playstation", StringComparison.OrdinalIgnoreCase))
            return "TV / console";
        if (platform.Contains("car", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("automotive", StringComparison.OrdinalIgnoreCase))
            return "Car";
        // Spotify writes macOS as "OS X 10.15.7 [x86_64]" - with a space - so match both forms.
        if (platform.Contains("windows", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("osx", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("os x", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("mac", StringComparison.OrdinalIgnoreCase) ||
            platform.Contains("linux", StringComparison.OrdinalIgnoreCase))
            return "Desktop";
        return "Other";
    }

    private sealed class YearAccumulator
    {
        public long Ms;
        public int Plays;
        public readonly Dictionary<string, long> ArtistMs = new(StringComparer.Ordinal);
        public readonly Dictionary<string, long> TrackMs = new(StringComparer.Ordinal);
    }

    private static YearStat BuildYearStat(int year, YearAccumulator acc)
    {
        string topArtist = "-";
        long best = -1;
        foreach (var (name, ms) in acc.ArtistMs)
        {
            if (ms > best)
            {
                best = ms;
                topArtist = name;
            }
        }

        string topTrack = "-";
        best = -1;
        foreach (var (key, ms) in acc.TrackMs)
        {
            if (ms > best)
            {
                best = ms;
                // Track keys are "track\nartist"; show just the track name.
                topTrack = key[..key.IndexOf('\n')];
            }
        }

        return new YearStat
        {
            Year = year,
            TotalMsPlayed = acc.Ms,
            PlayCount = acc.Plays,
            UniqueArtists = acc.ArtistMs.Count,
            UniqueTracks = acc.TrackMs.Count,
            TopArtist = topArtist,
            TopTrack = topTrack,
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

    /// <summary>Length of the run of consecutive listening days ending on the last listening day.</summary>
    private static int CurrentStreak(IReadOnlyList<DateTimePoint> dayPoints)
    {
        if (dayPoints.Count == 0)
            return 0;

        int streak = 1;
        for (int i = dayPoints.Count - 1; i > 0; i--)
        {
            if (dayPoints[i].Date == dayPoints[i - 1].Date.AddDays(1))
                streak++;
            else
                break;
        }
        return streak;
    }

    /// <summary>
    /// Groups timestamped plays into sessions. A play whose start (end time minus duration)
    /// falls within <see cref="SessionGap"/> of the previous play's end continues the session.
    /// </summary>
    private static (int count, long avgMs, long longestMs, DateTime? longestDate) ComputeSessions(
        List<(DateTime End, int Ms)> plays)
    {
        if (plays.Count == 0)
            return (0, 0, 0, null);

        plays.Sort(static (a, b) => a.End.CompareTo(b.End));

        int count = 1;
        long totalMs = plays[0].Ms;
        long currentMs = plays[0].Ms;
        long longestMs = 0;
        DateTime sessionStart = StartOf(plays[0]);
        DateTime longestStart = sessionStart;
        DateTime prevEnd = plays[0].End;

        for (int i = 1; i < plays.Count; i++)
        {
            var start = StartOf(plays[i]);
            if (start - prevEnd > SessionGap)
            {
                if (currentMs > longestMs)
                {
                    longestMs = currentMs;
                    longestStart = sessionStart;
                }
                count++;
                currentMs = 0;
                sessionStart = start;
            }

            currentMs += plays[i].Ms;
            totalMs += plays[i].Ms;
            if (plays[i].End > prevEnd)
                prevEnd = plays[i].End;
        }

        if (currentMs > longestMs)
        {
            longestMs = currentMs;
            longestStart = sessionStart;
        }

        return (count, totalMs / count, longestMs, longestStart.Date);
    }

    private static DateTime StartOf((DateTime End, int Ms) play)
    {
        // Timestamps mark when a play ended; subtract the duration to get its start,
        // clamped so contrived data near DateTime.MinValue can't underflow.
        long ticks = play.End.Ticks - play.Ms * TimeSpan.TicksPerMillisecond;
        return new DateTime(Math.Max(0L, ticks));
    }
}
