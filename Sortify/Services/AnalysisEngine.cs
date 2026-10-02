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
    /// <summary>
    /// Default gap between plays that starts a new listening session. Configurable per run;
    /// see the <c>sessionGap</c> parameter on <see cref="Analyze"/>.
    /// </summary>
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);

    /// <summary>Plays a track needs before it can count as a forgotten favorite.</summary>
    public const int ForgottenMinTrackPlays = 20;

    /// <summary>Plays an artist needs before it can count as a forgotten favorite.</summary>
    public const int ForgottenMinArtistPlays = 50;

    /// <summary>How long a favorite has to go unplayed, before the end of the history, to count as forgotten.</summary>
    public const int ForgottenAfterDays = 180;

    /// <summary>How many forgotten tracks and artists are listed.</summary>
    public const int ForgottenMaxRows = 50;

    /// <summary>Runs aggregation on a background thread so the UI stays responsive.</summary>
    public static Task<AnalysisResult> AnalyzeAsync(
        IReadOnlyList<PlayRecord> records,
        FilterOptions filter,
        TimeSpan? sessionGap = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Analyze(records, filter, sessionGap, cancellationToken), cancellationToken);
    }

    /// <param name="sessionGap">
    /// Silence longer than this ends a listening session. Defaults to
    /// <see cref="SessionGap"/> when null.
    /// </param>
    public static AnalysisResult Analyze(
        IReadOnlyList<PlayRecord> records,
        FilterOptions filter,
        TimeSpan? sessionGap = null,
        CancellationToken cancellationToken = default)
    {
        // Tracks and albums are keyed by (name, artist) so the same title by two artists stays
        // two rows. A tuple rather than a joined string: joining allocates a fresh key for
        // every play, which on a large history is most of what a pass allocates.
        var artists = new Dictionary<string, ArtistStat>(StringComparer.Ordinal);
        var tracks = new Dictionary<(string Track, string Artist), TrackStat>();
        var albums = new Dictionary<(string Album, string Artist), AlbumStat>();
        var skips = new Dictionary<(string Track, string Artist), (int Plays, int Skips)>();
        var reasons = new Dictionary<string, ReasonEndStat>(StringComparer.OrdinalIgnoreCase);
        var years = new Dictionary<int, PeriodAccumulator>();
        var months = new Dictionary<DateTime, PeriodAccumulator>();
        var byDay = new Dictionary<DateTime, long>();
        var byHour = new long[24];
        var byDow = new long[7];
        var byDowHour = new long[7, 24];
        var timedPlays = new List<(DateTime End, int Ms)>();

        var shows = new Dictionary<string, ShowStat>(StringComparer.Ordinal);
        var showEpisodeKeys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var episodes = new Dictionary<(string Episode, string Show), EpisodeStat>();
        var platforms = new Dictionary<string, ContextStat>(StringComparer.Ordinal);
        var countries = new Dictionary<string, ContextStat>(StringComparer.OrdinalIgnoreCase);
        var families = new Dictionary<string, string>(StringComparer.Ordinal);

        int totalPlays = 0;
        long totalMs = 0;
        int skipEligiblePlays = 0;
        int totalSkips = 0;
        int completedPlays = 0;
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
                {
                    // Only a few dozen distinct platform strings exist, so classify each once.
                    if (!families.TryGetValue(r.Platform, out var family))
                    {
                        family = PlatformFamily(r.Platform);
                        families[r.Platform] = family;
                    }
                    Bump(platforms, family, r.MsPlayed);
                }
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

            var trackKey = (trackName, artistName);

            // ---- Relaxed-duration statistics: skips and end reasons -------------------------
            skipEligiblePlays++;
            if (r.Skipped)
                totalSkips++;
            if (string.Equals(r.ReasonEnd, "trackdone", StringComparison.OrdinalIgnoreCase))
                completedPlays++;

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

            if (!tracks.TryGetValue(trackKey, out var track))
            {
                track = new TrackStat { Track = trackName, Artist = artistName };
                tracks[trackKey] = track;
            }
            track.TotalMsPlayed += r.MsPlayed;
            track.PlayCount++;
            if (track.Uri.Length == 0)
                track.Uri = r.Uri;

            var albumKey = (albumName, artistName);
            if (!albums.TryGetValue(albumKey, out var album))
            {
                album = new AlbumStat { Album = albumName, Artist = artistName };
                albums[albumKey] = album;
            }
            album.TotalMsPlayed += r.MsPlayed;
            album.PlayCount++;

            var ts = r.Timestamp;
            if (ts != DateTime.MinValue)
            {
                if (artist.FirstPlayed is null || ts < artist.FirstPlayed)
                {
                    artist.FirstPlayed = ts;
                    artist.FirstTrack = trackName;
                }
                if (artist.LastPlayed is null || ts > artist.LastPlayed) artist.LastPlayed = ts;

                if (track.FirstPlayed is null || ts < track.FirstPlayed) track.FirstPlayed = ts;
                if (track.LastPlayed is null || ts > track.LastPlayed) track.LastPlayed = ts;

                if (album.FirstPlayed is null || ts < album.FirstPlayed) album.FirstPlayed = ts;
                if (album.LastPlayed is null || ts > album.LastPlayed) album.LastPlayed = ts;

                if (firstListen is null || ts < firstListen) firstListen = ts;
                if (lastListen is null || ts > lastListen) lastListen = ts;

                int hour = ts.Hour;
                int dow = (int)ts.DayOfWeek;
                CollectionsMarshal.GetValueRefOrAddDefault(byDay, ts.Date, out _) += r.MsPlayed;
                byHour[hour] += r.MsPlayed;
                byDow[dow] += r.MsPlayed;
                byDowHour[dow, hour] += r.MsPlayed;

                timedPlays.Add((ts, r.MsPlayed));

                int year = ts.Year;
                if (!years.TryGetValue(year, out var acc))
                {
                    acc = new PeriodAccumulator();
                    years[year] = acc;
                }
                acc.Add(artist, track, r.MsPlayed);

                var month = new DateTime(ts.Year, ts.Month, 1);
                if (!months.TryGetValue(month, out var monthAcc))
                {
                    monthAcc = new PeriodAccumulator();
                    months[month] = monthAcc;
                }
                monthAcc.Add(artist, track, r.MsPlayed);
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
            skippedList.Add(new SkippedTrackStat
            {
                Track = key.Track,
                Artist = key.Artist,
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
        var (breakDays, breakStart, breakEnd) = LongestBreak(dayPoints);

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

        var (sessionCount, avgSessionMs, longestSessionMs, longestSessionDate) =
            ComputeSessions(timedPlays, sessionGap ?? SessionGap);

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
        var monthList = months
            .OrderBy(kv => kv.Key)
            .Select(kv => BuildMonthStat(kv.Key, kv.Value))
            .ToList();

        // Measured from the last play in the results rather than from today: an export is a
        // snapshot, and a filtered one can end years ago.
        var forgottenBefore = lastListen?.AddDays(-ForgottenAfterDays);
        var forgottenTracks = forgottenBefore is { } trackCutoff
            ? tracksByCount
                .Where(t => t.PlayCount >= ForgottenMinTrackPlays && t.LastPlayed < trackCutoff)
                .Take(ForgottenMaxRows)
                .ToList()
            : new List<TrackStat>();
        var forgottenArtists = forgottenBefore is { } artistCutoff
            ? artistsByCount
                .Where(a => a.PlayCount >= ForgottenMinArtistPlays && a.LastPlayed < artistCutoff)
                .Take(ForgottenMaxRows)
                .ToList()
            : new List<ArtistStat>();

        return new AnalysisResult
        {
            Artists = artistList,
            Tracks = trackList,
            Albums = albumList,
            ArtistsByPlayCount = artistsByCount,
            TracksByPlayCount = tracksByCount,
            AlbumsByPlayCount = albumsByCount,
            Years = yearList,
            Months = monthList,
            ForgottenTracks = forgottenTracks,
            ForgottenArtists = forgottenArtists,
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
            LongestBreakDays = breakDays,
            LongestBreakStart = breakStart,
            LongestBreakEnd = breakEnd,
            BiggestDay = biggestDay,
            BiggestDayMs = biggestDayMs,
            CompletedPlays = completedPlays,
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
        Dictionary<(string Episode, string Show), EpisodeStat> episodes,
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

        var episodeKey = (r.EpisodeName, r.ShowName);
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
    /// models and SDK versions) into a handful of families worth charting. Matches whole
    /// tokens, so "tv" means a TV and not any word that happens to contain those letters.
    /// </summary>
    internal static string PlatformFamily(string platform)
    {
        string lower = platform.ToLowerInvariant();
        var tokens = lower.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);

        if (lower.Contains("web_player") || lower.Contains("webplayer"))
            return "Web player";

        // TVs, consoles and cars report as "Partner <device> ...", so they have to be matched
        // before the catch-all partner token sends them to the speaker bucket.
        if (lower.Contains("android_auto") || lower.Contains("android auto") || tokens.Any(CarTokens.Contains))
            return "Car";
        if (tokens.Any(TvTokens.Contains))
            return "TV / console";
        if (tokens.Any(SpeakerTokens.Contains))
            return "Speaker / cast";
        if (tokens.Any(MobileTokens.Contains))
            return "Mobile";

        // Spotify writes macOS as "OS X 10.15.7 [x86_64]", with a space.
        if (tokens.Any(DesktopTokens.Contains) || lower.Contains("os x"))
            return "Desktop";
        return "Other";
    }

    private static readonly char[] TokenSeparators =
        " _-;:,.()[]/\\".ToCharArray();

    private static readonly HashSet<string> CarTokens = new(StringComparer.Ordinal)
    {
        "car", "carplay", "automotive", "androidauto",
    };

    private static readonly HashSet<string> TvTokens = new(StringComparer.Ordinal)
    {
        "tv", "smarttv", "androidtv", "appletv", "firetv", "googletv", "tizen", "webos", "roku", "bravia",
        "xbox", "playstation", "ps3", "ps4", "ps5",
    };

    private static readonly HashSet<string> SpeakerTokens = new(StringComparer.Ordinal)
    {
        "cast", "chromecast", "sonos", "speaker", "echo", "alexa", "homepod", "bose", "partner",
    };

    private static readonly HashSet<string> MobileTokens = new(StringComparer.Ordinal)
    {
        "android", "ios", "iphone", "ipad", "ipod",
    };

    private static readonly HashSet<string> DesktopTokens = new(StringComparer.Ordinal)
    {
        "windows", "osx", "macos", "mac", "linux",
    };

    /// <summary>
    /// Running totals for one year or one month, and who led it. Keyed by the artist and
    /// track objects of the whole pass, which are already one per name, so each play costs a
    /// reference hash rather than hashing the names again for every month and year.
    /// </summary>
    private sealed class PeriodAccumulator
    {
        public long Ms;
        public int Plays;
        public readonly Dictionary<ArtistStat, long> ArtistMs = new(ReferenceEqualityComparer.Instance);
        public readonly Dictionary<TrackStat, long> TrackMs = new(ReferenceEqualityComparer.Instance);

        public void Add(ArtistStat artist, TrackStat track, int ms)
        {
            Ms += ms;
            Plays++;
            CollectionsMarshal.GetValueRefOrAddDefault(ArtistMs, artist, out _) += ms;
            CollectionsMarshal.GetValueRefOrAddDefault(TrackMs, track, out _) += ms;
        }

        public (string Artist, long Ms) TopArtist()
        {
            (string, long) top = ("-", 0);
            long best = -1;
            foreach (var (artist, ms) in ArtistMs)
            {
                if (ms > best)
                {
                    best = ms;
                    top = (artist.Artist, ms);
                }
            }
            return top;
        }

        public ((string Track, string Artist) Key, long Ms) TopTrack()
        {
            ((string, string), long) top = (("-", "-"), 0);
            long best = -1;
            foreach (var (track, ms) in TrackMs)
            {
                if (ms > best)
                {
                    best = ms;
                    top = ((track.Track, track.Artist), ms);
                }
            }
            return top;
        }
    }

    private static YearStat BuildYearStat(int year, PeriodAccumulator acc) => new()
    {
        Year = year,
        TotalMsPlayed = acc.Ms,
        PlayCount = acc.Plays,
        UniqueArtists = acc.ArtistMs.Count,
        UniqueTracks = acc.TrackMs.Count,
        TopArtist = acc.TopArtist().Artist,
        TopTrack = acc.TopTrack().Key.Track,
    };

    private static MonthStat BuildMonthStat(DateTime month, PeriodAccumulator acc)
    {
        var (artist, artistMs) = acc.TopArtist();
        var (track, trackMs) = acc.TopTrack();
        return new MonthStat
        {
            Month = month,
            TotalMsPlayed = acc.Ms,
            PlayCount = acc.Plays,
            TopArtist = artist,
            TopArtistMs = artistMs,
            TopTrack = track.Track,
            TopTrackArtist = track.Artist,
            TopTrackMs = trackMs,
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

    /// <summary>
    /// Finds the longest stretch of silence: the biggest gap between two consecutive
    /// listening days. Returns the number of days with no listening, plus the active days
    /// that bracket it.
    /// </summary>
    private static (int days, DateTime? start, DateTime? end) LongestBreak(IReadOnlyList<DateTimePoint> dayPoints)
    {
        int best = 0;
        DateTime? bestStart = null, bestEnd = null;

        for (int i = 1; i < dayPoints.Count; i++)
        {
            // Consecutive days are one day apart, which is a gap of zero silent days.
            int gap = (int)(dayPoints[i].Date - dayPoints[i - 1].Date).TotalDays - 1;
            if (gap > best)
            {
                best = gap;
                bestStart = dayPoints[i - 1].Date;
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
    /// falls within <paramref name="gap"/> of the previous play's end continues the session.
    /// </summary>
    private static (int count, long avgMs, long longestMs, DateTime? longestDate) ComputeSessions(
        List<(DateTime End, int Ms)> plays, TimeSpan gap)
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
            if (start - prevEnd > gap)
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
