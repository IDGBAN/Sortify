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

/// <summary>Top-level result of an analysis pass over the filtered records.</summary>
public sealed class AnalysisResult
{
    public IReadOnlyList<ArtistStat> Artists { get; init; } = Array.Empty<ArtistStat>();
    public IReadOnlyList<TrackStat> Tracks { get; init; } = Array.Empty<TrackStat>();

    public int TotalPlays { get; init; }
    public long TotalMsPlayed { get; init; }
    public int UniqueArtists => Artists.Count;
    public int UniqueTracks => Tracks.Count;
    public DateTime? FirstListen { get; init; }
    public DateTime? LastListen { get; init; }

    /// <summary>ms played bucketed by calendar day (UTC).</summary>
    public IReadOnlyList<DateTimePoint> PlaytimeByDay { get; init; } = Array.Empty<DateTimePoint>();

    /// <summary>ms played indexed by hour of day 0-23.</summary>
    public long[] PlaytimeByHour { get; init; } = new long[24];

    /// <summary>ms played indexed by day of week 0=Sunday..6=Saturday.</summary>
    public long[] PlaytimeByDayOfWeek { get; init; } = new long[7];

    public TimeSpan TotalTime => TimeSpan.FromMilliseconds(TotalMsPlayed);

    public static AnalysisResult Empty { get; } = new();
}

/// <summary>A simple (date, value) pair for time-series charts.</summary>
public readonly record struct DateTimePoint(DateTime Date, double Value);
