namespace Sortify.Models;

/// <summary>Aggregated listening statistics for a single artist.</summary>
public sealed class ArtistStat
{
    public required string Artist { get; init; }
    public long TotalMsPlayed { get; set; }
    public int PlayCount { get; set; }
    public DateTime FirstPlayed { get; set; } = DateTime.MaxValue;
    public DateTime LastPlayed { get; set; } = DateTime.MinValue;

    /// <summary>The track that was playing at the artist's first recorded listen.</summary>
    public string FirstTrack { get; set; } = "Unknown Track";

    public TimeSpan TotalTime => TimeSpan.FromMilliseconds(TotalMsPlayed);
    public double TotalHours => TotalMsPlayed / 3_600_000d;
}

/// <summary>Aggregated listening statistics for a single track.</summary>
public sealed class TrackStat
{
    public required string Track { get; init; }
    public required string Artist { get; init; }
    public long TotalMsPlayed { get; set; }
    public int PlayCount { get; set; }
    public DateTime FirstPlayed { get; set; } = DateTime.MaxValue;
    public DateTime LastPlayed { get; set; } = DateTime.MinValue;

    public TimeSpan TotalTime => TimeSpan.FromMilliseconds(TotalMsPlayed);
    public double TotalHours => TotalMsPlayed / 3_600_000d;
}

/// <summary>Aggregated listening statistics for a single album.</summary>
public sealed class AlbumStat
{
    public required string Album { get; init; }
    public required string Artist { get; init; }
    public long TotalMsPlayed { get; set; }
    public int PlayCount { get; set; }
    public DateTime FirstPlayed { get; set; } = DateTime.MaxValue;
    public DateTime LastPlayed { get; set; } = DateTime.MinValue;

    public TimeSpan TotalTime => TimeSpan.FromMilliseconds(TotalMsPlayed);
    public double TotalHours => TotalMsPlayed / 3_600_000d;
}

/// <summary>
/// Skip counts for a single track. Computed with the minimum-duration filter relaxed,
/// because skipped plays are usually shorter than the cutoff and would otherwise vanish.
/// </summary>
public sealed class SkippedTrackStat
{
    public required string Track { get; init; }
    public required string Artist { get; init; }
    public int SkipCount { get; set; }
    public int PlayCount { get; set; }

    /// <summary>Share of this track's plays that were skipped (0-100).</summary>
    public double SkipRate => PlayCount == 0 ? 0 : SkipCount * 100.0 / PlayCount;
}

/// <summary>Top-level result of an analysis pass over the filtered records.</summary>
public sealed class AnalysisResult
{
    public IReadOnlyList<ArtistStat> Artists { get; init; } = Array.Empty<ArtistStat>();
    public IReadOnlyList<TrackStat> Tracks { get; init; } = Array.Empty<TrackStat>();
    public IReadOnlyList<AlbumStat> Albums { get; init; } = Array.Empty<AlbumStat>();

    public int TotalPlays { get; init; }
    public long TotalMsPlayed { get; init; }
    public int UniqueArtists => Artists.Count;
    public int UniqueTracks => Tracks.Count;
    public int UniqueAlbums => Albums.Count;
    public DateTime? FirstListen { get; init; }
    public DateTime? LastListen { get; init; }

    /// <summary>ms played bucketed by calendar day (local time).</summary>
    public IReadOnlyList<DateTimePoint> PlaytimeByDay { get; init; } = Array.Empty<DateTimePoint>();

    /// <summary>ms played indexed by hour of day 0-23 (local time).</summary>
    public long[] PlaytimeByHour { get; init; } = new long[24];

    /// <summary>ms played indexed by day of week 0=Sunday..6=Saturday (local time).</summary>
    public long[] PlaytimeByDayOfWeek { get; init; } = new long[7];

    /// <summary>ms played bucketed by [day of week 0=Sunday..6, hour of day 0-23] for the heatmap.</summary>
    public long[,] PlaytimeByDowHour { get; init; } = new long[7, 24];

    // ---- Skip statistics (computed with the min-duration filter relaxed) ----------------

    /// <summary>Tracks ordered by how often they were skipped.</summary>
    public IReadOnlyList<SkippedTrackStat> SkippedTracks { get; init; } = Array.Empty<SkippedTrackStat>();

    /// <summary>Total plays matching the filters when the minimum-duration cutoff is ignored.</summary>
    public int SkipEligiblePlays { get; init; }

    /// <summary>Plays Spotify flagged as skipped, out of <see cref="SkipEligiblePlays"/>.</summary>
    public int TotalSkips { get; init; }

    // ---- Streaks and records -------------------------------------------------------------

    /// <summary>Number of distinct calendar days with at least one counted play.</summary>
    public int ActiveDays { get; init; }

    /// <summary>Longest run of consecutive days with listening, in days.</summary>
    public int LongestStreakDays { get; init; }
    public DateTime? LongestStreakStart { get; init; }
    public DateTime? LongestStreakEnd { get; init; }

    /// <summary>The single calendar day with the most listening time.</summary>
    public DateTime? BiggestDay { get; init; }
    public long BiggestDayMs { get; init; }

    public TimeSpan TotalTime => TimeSpan.FromMilliseconds(TotalMsPlayed);

    public static AnalysisResult Empty { get; } = new();
}

/// <summary>A simple (date, value) pair for time-series charts.</summary>
public readonly record struct DateTimePoint(DateTime Date, double Value);
