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
        CancellationToken cancellationToken = default)
        => Task.Run(() => Build(records, filter, scope, title, subtitle, cancellationToken), cancellationToken);

    public static DetailResult Build(
        IReadOnlyList<PlayRecord> records,
        FilterOptions filter,
        DetailScope scope,
        string title,
        string subtitle,
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
        DateTime? first = null;
        DateTime? last = null;
        string uri = string.Empty;

        // A year scope carries its number in the title; parsing it per record would repeat
        // the same parse hundreds of thousands of times.
        int scopeYear = 0;
        if (scope == DetailScope.Year && !int.TryParse(title, out scopeYear))
            return Empty(scope, title, subtitle);

        // A show or episode is opened from the Podcasts tab, which lists them whether or not
        // they count toward the music statistics, so it can't depend on that setting.
        bool podcastScope = scope is DetailScope.Show or DetailScope.Episode;
        bool anyAudiobook = false, anyPodcast = false;

        int counter = 0;
        foreach (var r in FilterEngine.Apply(records, filter))
        {
            if ((++counter & 0x3FFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            // Mirror the main analysis: podcasts and audiobooks only feed these aggregates
            // when the user asked for them, otherwise a year's totals would disagree with
            // the Years tab they were opened from.
            if (r.Kind != ContentKind.Music && !filter.IncludePodcasts && !podcastScope)
                continue;

            string trackName = r.DisplayTrack, artistName = r.DisplayArtist, albumName = r.DisplayAlbum;

            if (!IsMatch(r, trackName, artistName, albumName, scope, title, subtitle, scopeYear))
                continue;

            totalMs += r.MsPlayed;
            playCount++;
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

            // Only a year spans multiple artists; for the other scopes this would be one
            // row repeating what the header already says.
            ArtistStat? artist = null;
            if (scope == DetailScope.Year)
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
        };
    }

    private static bool IsMatch(
        PlayRecord r, string track, string artist, string album,
        DetailScope scope, string title, string subtitle, int scopeYear)
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
