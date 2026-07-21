namespace Sortify.Models;

/// <summary>What a <see cref="DetailResult"/> was built for.</summary>
public enum DetailScope
{
    Artist,
    Track,
    Album,
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

    /// <summary>Distinct calendar days this had at least one play.</summary>
    public int ActiveDays { get; init; }

    /// <summary>Tracks that make up this selection, most listened first.</summary>
    public IReadOnlyList<TrackStat> Tracks { get; init; } = Array.Empty<TrackStat>();

    /// <summary>Albums that make up this selection, most listened first.</summary>
    public IReadOnlyList<AlbumStat> Albums { get; init; } = Array.Empty<AlbumStat>();

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
    /// Browser URL for this selection. Prefers the exact track when the export carried a
    /// URI, and otherwise falls back to a Spotify search, which always resolves to something.
    /// </summary>
    public string WebUrl
    {
        get
        {
            // "spotify:track:abc" -> "https://open.spotify.com/track/abc"
            if (Uri.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = Uri.Split(':');
                if (parts.Length >= 3 && parts[1].Length > 0 && parts[2].Length > 0)
                    return $"https://open.spotify.com/{parts[1]}/{parts[2]}";
            }

            var query = Subtitle.Length > 0 ? $"{Title} {Subtitle}" : Title;
            return "https://open.spotify.com/search/" + System.Uri.EscapeDataString(query);
        }
    }
}
