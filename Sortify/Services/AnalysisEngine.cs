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

    /// <summary>
    /// The stretch a burst of repeats is measured over, in days. 7 finds the week a song was
    /// on repeat; 1 finds single obsessive days; much past 30 just finds favourites.
    /// </summary>
    public const int RepeatWindowDays = 7;

    /// <summary>Plays inside the window a track needs to count as having been on repeat.</summary>
    public const int RepeatMinPlays = 10;

    /// <summary>How many tracks the On Repeat list shows.</summary>
    public const int RepeatRows = 25;

    /// <summary>How many of the top artists overall the rank history follows. Past ten the lines tangle.</summary>
    public const int RankHistoryArtists = 8;

    /// <summary>The lowest rank the rank history charts; an artist below it leaves a gap for that year.</summary>
    public const int RankHistoryFloor = 20;

    /// <summary>Play counts worth marking: the play that reached each one becomes a milestone.</summary>
    public static readonly int[] MilestonePlayCounts = { 1_000, 10_000, 50_000, 100_000, 250_000, 500_000 };

    /// <summary>Listening hours worth marking: the play that pushed the total past each one.</summary>
    public static readonly int[] MilestoneHours = { 100, 500, 1_000, 2_500, 5_000, 10_000 };

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
        // Each play costs a handful of dictionary lookups, and on a large history those lookups
        // are most of a pass, so the loop avoids every one it can: a track's skips are counted
        // on its own stat, its album is remembered from its last play, plays in the same month
        // as the one before reuse its totals, and years are summed from their months at the end.
        var artists = new Dictionary<string, ArtistStat>(StringComparer.Ordinal);
        var tracks = new Dictionary<(string Track, string Artist), TrackStat>();
        var albums = new Dictionary<(string Album, string Artist), AlbumStat>();
        var reasons = new Dictionary<string, ReasonEndStat>(StringComparer.OrdinalIgnoreCase);
        var months = new Dictionary<DateTime, PeriodAccumulator>();
        PeriodAccumulator? monthAcc = null;
        var month = DateTime.MinValue;

        // Tracks in the order their first counted play came. Short plays put a track in
        // tracks for its skips, so this is what keeps the rankings to tracks actually counted.
        var countedTracks = new List<TrackStat>();
        var byDay = new Dictionary<DateTime, (long Ms, int Plays)>();
        var byHour = new long[24];
        var byDow = new long[7];
        var byDowHour = new long[7, 24];
        var playsByHour = new int[24];
        var playsByDow = new int[7];
        var playsByDowHour = new int[7, 24];
        var timedPlays = new List<TimedPlay>();

        // Which tracks each album was heard through. Keyed by the stat objects, which are one
        // per name already, so a lookup costs two reference hashes rather than three string ones.
        var albumTracks = new HashSet<(AlbumStat, TrackStat)>();

        var shows = new Dictionary<string, ShowStat>(StringComparer.Ordinal);
        var showEpisodeKeys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var episodes = new Dictionary<(string Episode, string Show), EpisodeStat>();
        var platforms = new Dictionary<string, ContextStat>(StringComparer.Ordinal);
        var countries = new Dictionary<string, ContextStat>(StringComparer.OrdinalIgnoreCase);

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
                    Bump(platforms, Devices.FamilyOf(r.Platform), r.MsPlayed);
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

            string trackName = r.DisplayTrack;
            string artistName = r.DisplayArtist;
            string albumName = r.DisplayAlbum;

            ref var trackSlot = ref CollectionsMarshal.GetValueRefOrAddDefault(tracks, (trackName, artistName), out _);
            var track = trackSlot ??= new TrackStat { Track = trackName, Artist = artistName };

            // ---- Relaxed-duration statistics: skips and end reasons -------------------------
            skipEligiblePlays++;
            track.SkipEligiblePlays++;
            if (r.Skipped)
            {
                totalSkips++;
                track.Skips++;
            }
            if (string.Equals(r.ReasonEnd, "trackdone", StringComparison.OrdinalIgnoreCase))
                completedPlays++;

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

            if (track.PlayCount == 0)
                countedTracks.Add(track);
            track.TotalMsPlayed += r.MsPlayed;
            track.PlayCount++;
            if (track.Uri.Length == 0)
                track.Uri = r.Uri;

            // The track and the album share the play's artist, so the album the track was last
            // counted toward is this one whenever the names match.
            var album = track.CountedAlbum;
            if (album is null || !string.Equals(album.Album, albumName, StringComparison.Ordinal))
            {
                var albumKey = (albumName, artistName);
                if (!albums.TryGetValue(albumKey, out album))
                {
                    album = new AlbumStat { Album = albumName, Artist = artistName };
                    albums[albumKey] = album;
                }
                track.CountedAlbum = album;
                if (albumTracks.Add((album, track)))
                    album.TrackCount++;
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
                ref var day = ref CollectionsMarshal.GetValueRefOrAddDefault(byDay, ts.Date, out _);
                day.Ms += r.MsPlayed;
                day.Plays++;
                byHour[hour] += r.MsPlayed;
                byDow[dow] += r.MsPlayed;
                byDowHour[dow, hour] += r.MsPlayed;
                playsByHour[hour]++;
                playsByDow[dow]++;
                playsByDowHour[dow, hour]++;

                timedPlays.Add(new TimedPlay(ts, r.MsPlayed, track));

                if (monthAcc is null || ts.Year != month.Year || ts.Month != month.Month)
                {
                    month = new DateTime(ts.Year, ts.Month, 1);
                    ref var monthSlot = ref CollectionsMarshal.GetValueRefOrAddDefault(months, month, out _);
                    monthAcc = monthSlot ??= new PeriodAccumulator();
                }
                monthAcc.Add(artist, track, r.MsPlayed);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // The per-row figures the tables show beyond time and plays.
        foreach (var track in countedTracks)
        {
            track.SkipRate = track.SkipEligiblePlays == 0 ? 0 : track.Skips * 100.0 / track.SkipEligiblePlays;
            artists[track.Artist].TrackCount++;
        }
        foreach (var monthTotals in months.Values)
        {
            foreach (var artist in monthTotals.Artists.Keys)
                artist.MonthCount++;
        }

        // In month order, so each year meets its artists and tracks in the order they were
        // first played, as the months do.
        var years = new Dictionary<int, PeriodAccumulator>();
        foreach (var (start, monthTotals) in months.OrderBy(kv => kv.Key))
        {
            ref var yearSlot = ref CollectionsMarshal.GetValueRefOrAddDefault(years, start.Year, out _);
            (yearSlot ??= new PeriodAccumulator()).Add(monthTotals);
        }

        var artistList = artists.Values.OrderByDescending(a => a.TotalMsPlayed).ToList();
        var trackList = countedTracks.OrderByDescending(t => t.TotalMsPlayed).ToList();
        var albumList = albums.Values.OrderByDescending(a => a.TotalMsPlayed).ToList();
        var artistsByCount = artists.Values.OrderByDescending(a => a.PlayCount).ToList();
        var tracksByCount = countedTracks.OrderByDescending(t => t.PlayCount).ToList();
        var albumsByCount = albums.Values.OrderByDescending(a => a.PlayCount).ToList();
        var skippedList = new List<SkippedTrackStat>();
        foreach (var track in tracks.Values)
        {
            if (track.Skips == 0)
                continue;
            skippedList.Add(new SkippedTrackStat
            {
                Track = track.Track,
                Artist = track.Artist,
                SkipCount = track.Skips,
                PlayCount = track.SkipEligiblePlays,
            });
        }
        skippedList.Sort(static (a, b) => b.SkipCount.CompareTo(a.SkipCount));

        var reasonList = reasons.Values.OrderByDescending(x => x.Count).ToList();

        var showList = shows.Values.OrderByDescending(s => s.TotalMsPlayed).ToList();
        var episodeList = episodes.Values.OrderByDescending(e => e.TotalMsPlayed).ToList();
        // Sorted from the lists above, so shows played equally often stay in listening-time order.
        var showsByCount = showList.OrderByDescending(s => s.PlayCount).ToList();
        var episodesByCount = episodeList.OrderByDescending(e => e.PlayCount).ToList();
        var platformList = platforms.Values.OrderByDescending(p => p.TotalMsPlayed).ToList();
        var countryList = countries.Values.OrderByDescending(c => c.TotalMsPlayed).ToList();

        var days = byDay.OrderBy(kv => kv.Key).ToList();
        var dayPoints = days.Select(kv => new DateTimePoint(kv.Key, kv.Value.Ms / 3_600_000d)).ToList();
        var playDayPoints = days.Select(kv => new DateTimePoint(kv.Key, kv.Value.Plays)).ToList();

        var (streakDays, streakStart, streakEnd) = LongestStreak(dayPoints.Select(p => p.Date).ToList());
        int currentStreak = CurrentStreak(dayPoints);
        var (breakDays, breakStart, breakEnd) = LongestBreak(dayPoints);

        DateTime? biggestDay = null;
        long biggestDayMs = 0;
        foreach (var (day, totals) in byDay)
        {
            if (totals.Ms > biggestDayMs)
            {
                biggestDayMs = totals.Ms;
                biggestDay = day;
            }
        }

        timedPlays.Sort(static (a, b) => a.End.CompareTo(b.End));
        var (sessionCount, avgSessionMs, longestSessionMs, longestSessionDate) =
            ComputeSessions(timedPlays, sessionGap ?? SessionGap);
        var milestones = ComputeMilestones(timedPlays, artistList, tracks);
        var onRepeat = ComputeRepeats(timedPlays);

        var newArtistsByMonth = NewArtistsByMonth(artistList, firstListen, lastListen);

        var yearList = years
            .OrderBy(kv => kv.Key)
            .Select(kv => BuildYearStat(kv.Key, kv.Value))
            .ToList();
        var monthList = months
            .OrderBy(kv => kv.Key)
            .Select(kv => BuildMonthStat(kv.Key, kv.Value))
            .ToList();
        var rankHistory = ComputeRankHistory(years, yearList, artistList, Metric.Time);
        var rankHistoryByPlays = ComputeRankHistory(years, yearList, artistsByCount, Metric.Plays);

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
            RankHistory = rankHistory,
            RankHistoryByPlays = rankHistoryByPlays,
            Months = monthList,
            Milestones = milestones,
            OnRepeat = onRepeat,
            ForgottenTracks = forgottenTracks,
            ForgottenArtists = forgottenArtists,
            ReasonEnds = reasonList,
            SkippedTracks = skippedList,
            Shows = showList,
            Episodes = episodeList,
            ShowsByPlayCount = showsByCount,
            EpisodesByPlayCount = episodesByCount,
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
            PlaysByDay = playDayPoints,
            PlaysByHour = playsByHour,
            PlaysByDayOfWeek = playsByDow,
            PlaysByDowHour = playsByDowHour,
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
    /// Running totals for one year or one month, and who led it. Keyed by the artist and
    /// track objects of the whole pass, which are already one per name, so each play costs a
    /// reference hash rather than hashing the names again for every month and year.
    /// </summary>
    private sealed class PeriodAccumulator
    {
        public long Ms;
        public int Plays;
        public readonly Dictionary<ArtistStat, (long Ms, int Plays)> Artists = new(ReferenceEqualityComparer.Instance);
        public readonly Dictionary<TrackStat, (long Ms, int Plays)> Tracks = new(ReferenceEqualityComparer.Instance);

        public void Add(ArtistStat artist, TrackStat track, int ms)
        {
            Ms += ms;
            Plays++;
            ref var artistTotals = ref CollectionsMarshal.GetValueRefOrAddDefault(Artists, artist, out _);
            artistTotals.Ms += ms;
            artistTotals.Plays++;
            ref var trackTotals = ref CollectionsMarshal.GetValueRefOrAddDefault(Tracks, track, out _);
            trackTotals.Ms += ms;
            trackTotals.Plays++;
        }

        public void Add(PeriodAccumulator other)
        {
            Ms += other.Ms;
            Plays += other.Plays;
            foreach (var (artist, totals) in other.Artists)
            {
                ref var artistTotals = ref CollectionsMarshal.GetValueRefOrAddDefault(Artists, artist, out _);
                artistTotals.Ms += totals.Ms;
                artistTotals.Plays += totals.Plays;
            }
            foreach (var (track, totals) in other.Tracks)
            {
                ref var trackTotals = ref CollectionsMarshal.GetValueRefOrAddDefault(Tracks, track, out _);
                trackTotals.Ms += totals.Ms;
                trackTotals.Plays += totals.Plays;
            }
        }

        public (ArtistStat? Artist, long Ms, int Plays) TopArtist(Metric metric) => Leader(Artists, metric);

        public (TrackStat? Track, long Ms, int Plays) TopTrack(Metric metric) => Leader(Tracks, metric);

        /// <summary>
        /// The entry listened to longest, or played most often. Plays tie far more often than
        /// milliseconds do, so a tie on plays goes to the one listened to longer.
        /// </summary>
        private static (T? Key, long Ms, int Plays) Leader<T>(Dictionary<T, (long Ms, int Plays)> totals, Metric metric)
            where T : class
        {
            T? top = null;
            (long Ms, int Plays) best = (-1, -1);
            foreach (var (key, value) in totals)
            {
                bool better = metric == Metric.Plays
                    ? value.Plays > best.Plays || (value.Plays == best.Plays && value.Ms > best.Ms)
                    : value.Ms > best.Ms;
                if (better)
                {
                    top = key;
                    best = value;
                }
            }
            return top is null ? (null, 0, 0) : (top, best.Ms, best.Plays);
        }
    }

    /// <summary>
    /// Artists heard for the first time in each calendar month from the first play to the
    /// last, with a zero for a month that brought none, so the chart's line doesn't slope
    /// across it as if it had.
    /// </summary>
    private static List<DateTimePoint> NewArtistsByMonth(IReadOnlyList<ArtistStat> artists, DateTime? first, DateTime? last)
    {
        var points = new List<DateTimePoint>();
        if (first is not { } from || last is not { } to)
            return points;

        var counts = new Dictionary<DateTime, int>();
        foreach (var artist in artists)
        {
            if (artist.FirstPlayed is { } heard)
                CollectionsMarshal.GetValueRefOrAddDefault(counts, new DateTime(heard.Year, heard.Month, 1), out _)++;
        }

        // Counted rather than stepped to the last month, which in December 9999 has no next one.
        var start = new DateTime(from.Year, from.Month, 1);
        int months = (to.Year - from.Year) * 12 + to.Month - from.Month + 1;
        for (int i = 0; i < months; i++)
        {
            var month = start.AddMonths(i);
            points.Add(new DateTimePoint(month, counts.GetValueOrDefault(month)));
        }
        return points;
    }

    /// <summary>
    /// Where each of the top artists overall ranked by listening time, or by plays, in every
    /// year, down to <see cref="RankHistoryFloor"/>. Needs two years to say anything.
    /// </summary>
    /// <param name="artists">Every artist, ranked by <paramref name="metric"/>.</param>
    private static List<ArtistRanks> ComputeRankHistory(
        Dictionary<int, PeriodAccumulator> years, IReadOnlyList<YearStat> yearList, IReadOnlyList<ArtistStat> artists,
        Metric metric)
    {
        if (yearList.Count < 2)
            return new List<ArtistRanks>();

        var followed = artists.Take(RankHistoryArtists).ToList();
        var ranks = new Dictionary<ArtistStat, int?[]>(ReferenceEqualityComparer.Instance);
        foreach (var artist in followed)
            ranks[artist] = new int?[yearList.Count];

        for (int y = 0; y < yearList.Count; y++)
        {
            var inYear = years[yearList[y].Year].Artists;
            // Plays tie far more often than milliseconds do; the longer listen goes first, as with the leaders.
            var ranked = metric == Metric.Plays
                ? inYear.OrderByDescending(kv => kv.Value.Plays).ThenByDescending(kv => kv.Value.Ms)
                : inYear.OrderByDescending(kv => kv.Value.Ms);
            int rank = 0;
            foreach (var (artist, _) in ranked.Take(RankHistoryFloor))
            {
                rank++;
                if (ranks.TryGetValue(artist, out var row))
                    row[y] = rank;
            }
        }

        return followed.Select(a => new ArtistRanks(a, ranks[a])).ToList();
    }

    private static YearStat BuildYearStat(int year, PeriodAccumulator acc) => new()
    {
        Year = year,
        TotalMsPlayed = acc.Ms,
        PlayCount = acc.Plays,
        UniqueArtists = acc.Artists.Count,
        UniqueTracks = acc.Tracks.Count,
        TopArtist = acc.TopArtist(Metric.Time).Artist?.Artist ?? "-",
        TopTrack = acc.TopTrack(Metric.Time).Track?.Track ?? "-",
        MostPlayedArtist = acc.TopArtist(Metric.Plays).Artist?.Artist ?? "-",
        MostPlayedTrack = acc.TopTrack(Metric.Plays).Track?.Track ?? "-",
    };

    private static MonthStat BuildMonthStat(DateTime month, PeriodAccumulator acc)
    {
        var (artist, artistMs, _) = acc.TopArtist(Metric.Time);
        var (track, trackMs, _) = acc.TopTrack(Metric.Time);
        var (playedArtist, _, artistPlays) = acc.TopArtist(Metric.Plays);
        var (playedTrack, _, trackPlays) = acc.TopTrack(Metric.Plays);
        return new MonthStat
        {
            Month = month,
            TotalMsPlayed = acc.Ms,
            PlayCount = acc.Plays,
            TopArtist = artist?.Artist ?? "-",
            TopArtistMs = artistMs,
            TopTrack = track?.Track ?? "-",
            TopTrackArtist = track?.Artist ?? "-",
            TopTrackMs = trackMs,
            MostPlayedArtist = playedArtist?.Artist ?? "-",
            MostPlayedArtistPlays = artistPlays,
            MostPlayedTrack = playedTrack?.Track ?? "-",
            MostPlayedTrackArtist = playedTrack?.Artist ?? "-",
            MostPlayedTrackPlays = trackPlays,
        };
    }

    /// <summary>Finds the longest run of consecutive days in sorted, distinct calendar days.</summary>
    internal static (int Days, DateTime? Start, DateTime? End) LongestStreak(IReadOnlyList<DateTime> days)
    {
        if (days.Count == 0)
            return (0, null, null);

        int best = 1, current = 1;
        DateTime bestStart = days[0], bestEnd = days[0];
        DateTime runStart = days[0];

        for (int i = 1; i < days.Count; i++)
        {
            if (days[i] == days[i - 1].AddDays(1))
            {
                current++;
            }
            else
            {
                current = 1;
                runStart = days[i];
            }

            if (current > best)
            {
                best = current;
                bestStart = runStart;
                bestEnd = days[i];
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

    /// <summary>A dated play, kept in time order for sessions and milestones.</summary>
    private readonly record struct TimedPlay(DateTime End, int Ms, TrackStat Track);

    /// <summary>
    /// Groups timestamped plays, sorted by end time, into sessions. A play whose start (end
    /// time minus duration) falls within <paramref name="gap"/> of the previous play's end
    /// continues the session.
    /// </summary>
    private static (int count, long avgMs, long longestMs, DateTime? longestDate) ComputeSessions(
        List<TimedPlay> plays, TimeSpan gap)
    {
        if (plays.Count == 0)
            return (0, 0, 0, null);

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

    /// <summary>
    /// The plays that mark the history: the first one, the ones that reached each round play
    /// count and each round number of hours, and the first time the top artist came on.
    /// Expects <paramref name="plays"/> sorted by end time.
    /// </summary>
    private static List<Milestone> ComputeMilestones(
        List<TimedPlay> plays, IReadOnlyList<ArtistStat> artists,
        Dictionary<(string Track, string Artist), TrackStat> tracks)
    {
        var milestones = new List<Milestone>();
        if (plays.Count == 0)
            return milestones;

        milestones.Add(new Milestone("First play", plays[0].End, plays[0].Track));

        int nextCount = 0, nextHours = 0;
        long ms = 0;
        for (int i = 0; i < plays.Count; i++)
        {
            ms += plays[i].Ms;
            if (nextCount < MilestonePlayCounts.Length && i + 1 == MilestonePlayCounts[nextCount])
            {
                milestones.Add(new Milestone($"{MilestonePlayCounts[nextCount]:N0}th play", plays[i].End, plays[i].Track));
                nextCount++;
            }
            while (nextHours < MilestoneHours.Length && ms >= MilestoneHours[nextHours] * 3_600_000L)
            {
                milestones.Add(new Milestone($"{MilestoneHours[nextHours]:N0} hours listened", plays[i].End, plays[i].Track));
                nextHours++;
            }
        }

        if (artists.Count > 0 && artists[0] is { FirstPlayed: { } first } top &&
            tracks.TryGetValue((top.FirstTrack, top.Artist), out var firstTrack))
        {
            milestones.Add(new Milestone($"First time hearing {top.Artist}, your top artist", first, firstTrack));
        }

        milestones.Sort(static (a, b) => a.Date.CompareTo(b.Date));
        return milestones;
    }

    /// <summary>
    /// Each track's busiest <see cref="RepeatWindowDays"/>: the most of its plays that ended
    /// within that span of each other, and the first and last of them. Expects
    /// <paramref name="plays"/> sorted by end time. Only tracks with enough plays overall to
    /// qualify are looked at, which keeps this to a small share of a large history.
    /// </summary>
    private static List<RepeatStat> ComputeRepeats(List<TimedPlay> plays)
    {
        var times = new Dictionary<TrackStat, List<DateTime>>(ReferenceEqualityComparer.Instance);
        foreach (var play in plays)
        {
            if (play.Track.PlayCount < RepeatMinPlays)
                continue;
            if (!times.TryGetValue(play.Track, out var list))
            {
                list = new List<DateTime>();
                times[play.Track] = list;
            }
            list.Add(play.End);
        }

        var window = TimeSpan.FromDays(RepeatWindowDays);
        var repeats = new List<RepeatStat>();
        foreach (var (track, ends) in times)
        {
            int best = 0, bestFirst = 0, bestLast = 0;
            for (int first = 0, last = 0; last < ends.Count; last++)
            {
                while (ends[last] - ends[first] >= window)
                    first++;
                if (last - first + 1 > best)
                {
                    best = last - first + 1;
                    bestFirst = first;
                    bestLast = last;
                }
            }

            if (best >= RepeatMinPlays)
                repeats.Add(new RepeatStat(track, best, ends[bestFirst], ends[bestLast]));
        }

        return repeats
            .OrderByDescending(r => r.Plays)
            .ThenBy(r => r.From)
            .Take(RepeatRows)
            .ToList();
    }

    private static DateTime StartOf(TimedPlay play)
    {
        // Timestamps mark when a play ended; subtract the duration to get its start,
        // clamped so contrived data near DateTime.MinValue can't underflow.
        long ticks = play.End.Ticks - play.Ms * TimeSpan.TicksPerMillisecond;
        return new DateTime(Math.Max(0L, ticks));
    }
}
