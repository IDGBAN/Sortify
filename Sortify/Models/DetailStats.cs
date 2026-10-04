namespace Sortify.Models;

/// <summary>What a <see cref="DetailResult"/> was built for.</summary>
public enum DetailScope
{
    Artist,
    Track,
    Album,

    /// <summary>One calendar year; <see cref="DetailResult.Title"/> holds the year number.</summary>
    Year,

    /// <summary>A podcast show or an audiobook.</summary>
    Show,

    /// <summary>One podcast episode or audiobook chapter; the subtitle holds its show.</summary>
    Episode,
}

/// <summary>
/// A focused breakdown of one artist, track or album, computed on demand when the user
/// drills into a grid row. Everything here respects the filters that were active at the time.
/// </summary>
public sealed class DetailResult
{
    public required DetailScope Scope { get; init; }

    /// <summary>Primary name: the artist, track or album that was opened.</summary>
    public required string Title { get; init; }

    /// <summary>Secondary line: the artist, for a track or album. Empty for an artist.</summary>
    public string Subtitle { get; init; } = string.Empty;

    public long TotalMsPlayed { get; init; }
    public int PlayCount { get; init; }
    public DateTime? FirstPlayed { get; init; }
    public DateTime? LastPlayed { get; init; }

    /// <summary>
    /// What kind of content a show or episode breakdown covers, so an audiobook is called one.
    /// Music for every other scope.
    /// </summary>
    public ContentKind Kind { get; init; } = ContentKind.Music;

    /// <summary>Distinct calendar days this had at least one play.</summary>
    public int ActiveDays { get; init; }

    /// <summary>Tracks that make up this selection, most listened first.</summary>
    public IReadOnlyList<TrackStat> Tracks { get; init; } = Array.Empty<TrackStat>();

    /// <summary>Albums that make up this selection, most listened first.</summary>
    public IReadOnlyList<AlbumStat> Albums { get; init; } = Array.Empty<AlbumStat>();

    /// <summary>
    /// Artists that make up this selection, most listened first. Only populated for scopes
    /// that can span more than one artist (currently <see cref="DetailScope.Year"/>).
    /// </summary>
    public IReadOnlyList<ArtistStat> Artists { get; init; } = Array.Empty<ArtistStat>();

    /// <summary>Listening time per calendar month, oldest first.</summary>
    public IReadOnlyList<DateTimePoint> ByMonth { get; init; } = Array.Empty<DateTimePoint>();

    /// <summary>ms played indexed by hour of day 0-23.</summary>
    public long[] ByHour { get; init; } = new long[24];

    /// <summary>
    /// A Spotify URI seen on one of these plays ("spotify:track:..."), when the export
    /// carried one. Empty for older exports and for anything Spotify didn't tag.
    /// </summary>
    public string Uri { get; init; } = string.Empty;

    public TimeSpan TotalTime => TimeSpan.FromMilliseconds(TotalMsPlayed);

    /// <summary>
    /// Browser URL for this selection. A track or episode opens its own page when the export
    /// carried a URI. Artists, albums and shows go through Spotify search instead: the export
    /// only records track and episode URIs, and opening one of those would land on a single
    /// song or episode. Empty for a year.
    /// </summary>
    public string WebUrl
    {
        get
        {
            if (Scope == DetailScope.Year)
                return string.Empty;

            if (Scope is DetailScope.Track or DetailScope.Episode && SpotifyLink.FromUri(Uri) is { Length: > 0 } link)
                return link;

            var query = Subtitle.Length > 0 ? $"{Title} {Subtitle}" : Title;
            return "https://open.spotify.com/search/" + System.Uri.EscapeDataString(query);
        }
    }
}
