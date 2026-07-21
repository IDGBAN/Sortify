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
        var tracks = new Dictionary<string, TrackStat>(StringComparer.Ordinal);
        var albums = new Dictionary<string, AlbumStat>(StringComparer.Ordinal);
        var byMonth = new Dictionary<DateTime, long>();
        var days = new HashSet<DateTime>();
        var byHour = new long[24];

        long totalMs = 0;
        int playCount = 0;
        DateTime? first = null;
        DateTime? last = null;
        string uri = string.Empty;

        int counter = 0;
        foreach (var r in FilterEngine.Apply(records, filter))
        {
            if ((++counter & 0x3FFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            if (!IsMatch(r, scope, title, subtitle))
                continue;

            totalMs += r.MsPlayed;
            playCount++;

            // Any URI from a matching play works; the first non-empty one wins.
            if (uri.Length == 0 && r.Uri.Length > 0)
                uri = r.Uri;

            var trackKey = r.TrackName + "\n" + r.ArtistName;
            if (!tracks.TryGetValue(trackKey, out var track))
            {
                track = new TrackStat { Track = r.TrackName, Artist = r.ArtistName };
                tracks[trackKey] = track;
            }
            track.TotalMsPlayed += r.MsPlayed;
            track.PlayCount++;

            var albumKey = r.AlbumName + "\n" + r.ArtistName;
            if (!albums.TryGetValue(albumKey, out var album))
            {
                album = new AlbumStat { Album = r.AlbumName, Artist = r.ArtistName };
                albums[albumKey] = album;
            }
            album.TotalMsPlayed += r.MsPlayed;
            album.PlayCount++;

            if (r.Timestamp == DateTime.MinValue)
                continue;

            if (first is null || r.Timestamp < first) first = r.Timestamp;
            if (last is null || r.Timestamp > last) last = r.Timestamp;
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
            TotalMsPlayed = totalMs,
            PlayCount = playCount,
            FirstPlayed = first,
            LastPlayed = last,
            ActiveDays = days.Count,
            Tracks = tracks.Values.OrderByDescending(t => t.TotalMsPlayed).ToList(),
            Albums = albums.Values.OrderByDescending(a => a.TotalMsPlayed).ToList(),
            ByMonth = byMonth.OrderBy(kv => kv.Key)
                .Select(kv => new DateTimePoint(kv.Key, kv.Value / 3_600_000d))
                .ToList(),
            ByHour = byHour,
            Uri = uri,
        };
    }

    private static bool IsMatch(PlayRecord r, DetailScope scope, string title, string subtitle) => scope switch
    {
        DetailScope.Artist => string.Equals(r.ArtistName, title, StringComparison.Ordinal),
        DetailScope.Track => string.Equals(r.TrackName, title, StringComparison.Ordinal)
                             && string.Equals(r.ArtistName, subtitle, StringComparison.Ordinal),
        DetailScope.Album => string.Equals(r.AlbumName, title, StringComparison.Ordinal)
                             && string.Equals(r.ArtistName, subtitle, StringComparison.Ordinal),
        _ => false,
    };
}
