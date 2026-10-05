using System.Globalization;
using Sortify.Models;

namespace Sortify.Services;

/// <summary>
/// Builds the focused breakdown shown when the user drills into a grid row. Runs a single
/// filtered pass over the raw records, so it is cheap enough to compute on double-click
/// rather than precomputing one of these per artist up front.
/// </summary>
public static class DetailEngine
{
    public static Task<DetailResult> BuildAsync(
        IReadOnlyList<PlayRecord> records,
        FilterOptions filter,
        DetailScope scope,
        string title,
        string subtitle,
        AnalysisResult? overall = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Build(records, filter, scope, title, subtitle, overall, cancellationToken), cancellationToken);

    /// <param name="overall">
    /// The results the breakdown was opened from, under the same filters. Where it ranks and its
    /// share of the listening are read from them; without them both are left out.
    /// </param>
    public static DetailResult Build(
        IReadOnlyList<PlayRecord> records,
        FilterOptions filter,
        DetailScope scope,
        string title,
        string subtitle,
        AnalysisResult? overall = null,
        CancellationToken cancellationToken = default)
    {
        var tracks = new Dictionary<(string Track, string Artist), TrackStat>();
        var albums = new Dictionary<(string Album, string Artist), AlbumStat>();
        var artists = new Dictionary<string, ArtistStat>(StringComparer.Ordinal);
        var byMonth = new Dictionary<DateTime, long>();
        var days = new HashSet<DateTime>();
        var byHour = new long[24];

        long totalMs = 0;
        int playCount = 0;
        int skipEligible = 0, skips = 0;
        DateTime? first = null;
        DateTime? last = null;
        string uri = string.Empty;
        var plays = new List<PlayRecord>();
        var deviceMs = new Dictionary<string, long>(StringComparer.Ordinal);

        // A year scope carries its number in the title; parsing it per record would repeat
        // the same parse hundreds of thousands of times.
        int scopeYear = 0;
        if (scope == DetailScope.Year && !int.TryParse(title, out scopeYear))
            return Empty(scope, title, subtitle);
        DateTime scopeDay = default;
        if (scope == DetailScope.Day &&
            !DateTime.TryParseExact(title, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out scopeDay))
            return Empty(scope, title, subtitle);

        // A show or episode is opened from the Podcasts tab, which lists them whether or not
        // they count toward the music statistics, so it can't depend on that setting.
        bool podcastScope = scope is DetailScope.Show or DetailScope.Episode;
        bool anyAudiobook = false, anyPodcast = false;

        // The duration cutoff is relaxed so skips are seen, as the Insights tab sees them, and
        // applied by hand to everything else.
        int counter = 0;
        foreach (var r in FilterEngine.Apply(records, filter, ignoreMinDuration: true))
        {
            if ((++counter & 0x3FFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            // Mirror the main analysis: podcasts and audiobooks only feed these aggregates
            // when the user asked for them, otherwise a year's totals would disagree with
            // the Years tab they were opened from.
            if (r.Kind != ContentKind.Music && !filter.IncludePodcasts && !podcastScope)
                continue;

            string trackName = r.DisplayTrack, artistName = r.DisplayArtist, albumName = r.DisplayAlbum;

            if (!IsMatch(r, trackName, artistName, albumName, scope, title, subtitle, scopeYear, scopeDay))
                continue;

            skipEligible++;
            if (r.Skipped)
                skips++;
            if (r.MsPlayed < filter.MinMsPlayed)
                continue;

            totalMs += r.MsPlayed;
            playCount++;
            plays.Add(r);
            if (r.Platform.Length > 0)
            {
                string family = Devices.FamilyOf(r.Platform);
                deviceMs[family] = deviceMs.GetValueOrDefault(family) + r.MsPlayed;
            }
            anyAudiobook |= r.Kind == ContentKind.Audiobook;
            anyPodcast |= r.Kind == ContentKind.Podcast;

            // Any URI from a matching play works; the first non-empty one wins.
            if (uri.Length == 0 && r.Uri.Length > 0)
                uri = r.Uri;

            var trackKey = (trackName, artistName);
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

            // Only a year or a day spans multiple artists; for the other scopes this would be
            // one row repeating what the header already says.
            ArtistStat? artist = null;
            if (scope is DetailScope.Year or DetailScope.Day)
            {
                if (!artists.TryGetValue(artistName, out artist))
                {
                    artist = new ArtistStat { Artist = artistName, FirstTrack = trackName };
                    artists[artistName] = artist;
                }
                artist.TotalMsPlayed += r.MsPlayed;
                artist.PlayCount++;
            }

            if (r.Timestamp == DateTime.MinValue)
                continue;

            if (first is null || r.Timestamp < first) first = r.Timestamp;
            if (last is null || r.Timestamp > last) last = r.Timestamp;

            if (artist is not null)
            {
                if (artist.FirstPlayed is null || r.Timestamp < artist.FirstPlayed)
                {
                    artist.FirstPlayed = r.Timestamp;
                    artist.FirstTrack = trackName;
                }
                if (artist.LastPlayed is null || r.Timestamp > artist.LastPlayed) artist.LastPlayed = r.Timestamp;
            }

            if (track.FirstPlayed is null || r.Timestamp < track.FirstPlayed) track.FirstPlayed = r.Timestamp;
            if (track.LastPlayed is null || r.Timestamp > track.LastPlayed) track.LastPlayed = r.Timestamp;
            if (album.FirstPlayed is null || r.Timestamp < album.FirstPlayed) album.FirstPlayed = r.Timestamp;
            if (album.LastPlayed is null || r.Timestamp > album.LastPlayed) album.LastPlayed = r.Timestamp;

            days.Add(r.Timestamp.Date);
            byHour[r.Timestamp.Hour] += r.MsPlayed;

            var month = new DateTime(r.Timestamp.Year, r.Timestamp.Month, 1);
            byMonth[month] = byMonth.TryGetValue(month, out var m) ? m + r.MsPlayed : r.MsPlayed;
        }

        var (streakDays, streakStart, streakEnd) = AnalysisEngine.LongestStreak(days.Order().ToList());
        var busiest = byMonth.Count == 0 ? (KeyValuePair<DateTime, long>?)null : byMonth.MaxBy(kv => kv.Value);
        var topDevice = deviceMs.Count == 0 ? (KeyValuePair<string, long>?)null : deviceMs.MaxBy(kv => kv.Value);
        long deviceTotal = deviceMs.Values.Sum();
        var (rank, rankOutOf, share) = Standing(overall, scope, title, subtitle, totalMs);

        return new DetailResult
        {
            Scope = scope,
            Title = title,
            Subtitle = subtitle,
            Kind = !podcastScope ? ContentKind.Music
                : anyAudiobook && !anyPodcast ? ContentKind.Audiobook
                : ContentKind.Podcast,
            TotalMsPlayed = totalMs,
            PlayCount = playCount,
            FirstPlayed = first,
            LastPlayed = last,
            ActiveDays = days.Count,
            Tracks = tracks.Values.OrderByDescending(t => t.TotalMsPlayed).ToList(),
            Albums = albums.Values.OrderByDescending(a => a.TotalMsPlayed).ToList(),
            Artists = artists.Values.OrderByDescending(a => a.TotalMsPlayed).ToList(),
            ByMonth = byMonth.OrderBy(kv => kv.Key)
                .Select(kv => new DateTimePoint(kv.Key, kv.Value / 3_600_000d))
                .ToList(),
            ByHour = byHour,
            Uri = uri,
            Rank = rank,
            RankOutOf = rankOutOf,
            SharePercent = share,
            SkipEligiblePlays = skipEligible,
            Skips = skips,
            BusiestMonth = busiest?.Key,
            BusiestMonthMs = busiest?.Value ?? 0,
            LongestStreakDays = streakDays,
            LongestStreakStart = streakStart,
            LongestStreakEnd = streakEnd,
            TopDevice = topDevice?.Key ?? string.Empty,
            TopDevicePercent = topDevice is { } device && deviceTotal > 0 ? device.Value * 100.0 / deviceTotal : 0,
            // Stable, so plays sharing a timestamp keep the order the export gave them.
            Plays = plays.OrderBy(p => p.Timestamp).ToList(),
        };
    }

    /// <summary>
    /// Where the selection ranks by listening time among everything of its kind in
    /// <paramref name="overall"/>, how many of those there are, and its share of their listening.
    /// A year is ranked against the other years; a show or episode against the podcast listening.
    /// </summary>
    private static (int? Rank, int OutOf, double SharePercent) Standing(
        AnalysisResult? overall, DetailScope scope, string title, string subtitle, long totalMs)
    {
        if (overall is null)
            return (null, 0, 0);

        int index = scope switch
        {
            DetailScope.Artist => IndexOf(overall.Artists, a => a.Artist == title),
            DetailScope.Track => IndexOf(overall.Tracks, t => t.Track == title && t.Artist == subtitle),
            DetailScope.Album => IndexOf(overall.Albums, a => a.Album == title && a.Artist == subtitle),
            DetailScope.Year => IndexOf(overall.Years.OrderByDescending(y => y.TotalMsPlayed).ToList(),
                y => y.Year.ToString(CultureInfo.InvariantCulture) == title),
            DetailScope.Show => IndexOf(overall.Shows, s => s.Show == title),
            DetailScope.Episode => IndexOf(overall.Episodes, e => e.Episode == title && e.Show == subtitle),
            DetailScope.Day => IndexOf(overall.PlaytimeByDay.OrderByDescending(p => p.Value).ToList(),
                p => TimeFormat.Day(p.Date) == title),
            _ => -1,
        };
        int outOf = scope switch
        {
            DetailScope.Artist => overall.Artists.Count,
            DetailScope.Track => overall.Tracks.Count,
            DetailScope.Album => overall.Albums.Count,
            DetailScope.Year => overall.Years.Count,
            DetailScope.Show => overall.Shows.Count,
            DetailScope.Episode => overall.Episodes.Count,
            DetailScope.Day => overall.PlaytimeByDay.Count,
            _ => 0,
        };
        long whole = scope is DetailScope.Show or DetailScope.Episode ? overall.PodcastMsPlayed : overall.TotalMsPlayed;
        double share = whole > 0 ? Math.Min(100, totalMs * 100.0 / whole) : 0;
        return (index >= 0 ? index + 1 : null, outOf, share);
    }

    private static int IndexOf<T>(IReadOnlyList<T> items, Func<T, bool> match)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (match(items[i]))
                return i;
        }
        return -1;
    }

    private static bool IsMatch(
        PlayRecord r, string track, string artist, string album,
        DetailScope scope, string title, string subtitle, int scopeYear, DateTime scopeDay)
    {
        return scope switch
        {
            DetailScope.Artist => string.Equals(artist, title, StringComparison.Ordinal),
            DetailScope.Track => string.Equals(track, title, StringComparison.Ordinal)
                                 && string.Equals(artist, subtitle, StringComparison.Ordinal),
            DetailScope.Album => string.Equals(album, title, StringComparison.Ordinal)
                                 && string.Equals(artist, subtitle, StringComparison.Ordinal),
            // Undated rows belong to no year.
            DetailScope.Year => r.Timestamp != DateTime.MinValue && r.Timestamp.Year == scopeYear,
            DetailScope.Day => r.Timestamp != DateTime.MinValue && r.Timestamp.Date == scopeDay,
            DetailScope.Show => r.Kind != ContentKind.Music && string.Equals(r.ShowName, title, StringComparison.Ordinal),
            DetailScope.Episode => r.Kind != ContentKind.Music
                                   && string.Equals(r.EpisodeName, title, StringComparison.Ordinal)
                                   && string.Equals(r.ShowName, subtitle, StringComparison.Ordinal),
            _ => false,
        };
    }

    private static DetailResult Empty(DetailScope scope, string title, string subtitle)
        => new() { Scope = scope, Title = title, Subtitle = subtitle };
}
