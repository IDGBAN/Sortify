namespace Sortify.Models;

/// <summary>Aggregated listening statistics for a single artist.</summary>
public sealed class ArtistStat
{
    public required string Artist { get; init; }
    public long TotalMsPlayed { get; set; }
    public int PlayCount { get; set; }

    /// <summary>Null when no counted play of this artist carried a timestamp.</summary>
    public DateTime? FirstPlayed { get; set; }
    public DateTime? LastPlayed { get; set; }

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

    /// <summary>Null when no counted play of this track carried a timestamp.</summary>
    public DateTime? FirstPlayed { get; set; }
    public DateTime? LastPlayed { get; set; }

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

    /// <summary>Null when no counted play of this album carried a timestamp.</summary>
    public DateTime? FirstPlayed { get; set; }
    public DateTime? LastPlayed { get; set; }

    public TimeSpan TotalTime => TimeSpan.FromMilliseconds(TotalMsPlayed);
    public double TotalHours => TotalMsPlayed / 3_600_000d;
}

/// <summary>Aggregated listening statistics for a single calendar year.</summary>
public sealed class YearStat
{
    public required int Year { get; init; }
    public long TotalMsPlayed { get; set; }
    public int PlayCount { get; set; }
    public int UniqueArtists { get; set; }
    public int UniqueTracks { get; set; }
    public string TopArtist { get; set; } = "-";
    public string TopTrack { get; set; } = "-";

    public TimeSpan TotalTime => TimeSpan.FromMilliseconds(TotalMsPlayed);
    public double TotalHours => TotalMsPlayed / 3_600_000d;
}

/// <summary>Aggregated listening statistics for one podcast show or audiobook.</summary>
public sealed class ShowStat
{
    public required string Show { get; init; }
    public long TotalMsPlayed { get; set; }
    public int PlayCount { get; set; }

    /// <summary>Distinct episodes (or chapters) of this show that were played.</summary>
    public int EpisodeCount { get; set; }

    public DateTime? FirstPlayed { get; set; }
    public DateTime? LastPlayed { get; set; }

    /// <summary>Audiobooks are reported alongside podcasts but labelled separately.</summary>
    public ContentKind Kind { get; init; } = ContentKind.Podcast;

    public TimeSpan TotalTime => TimeSpan.FromMilliseconds(TotalMsPlayed);
    public double TotalHours => TotalMsPlayed / 3_600_000d;
}

/// <summary>Aggregated listening statistics for one podcast episode or audiobook chapter.</summary>
public sealed class EpisodeStat
{
    public required string Episode { get; init; }
    public required string Show { get; init; }
    public long TotalMsPlayed { get; set; }
    public int PlayCount { get; set; }

    public DateTime? FirstPlayed { get; set; }
    public DateTime? LastPlayed { get; set; }

    public TimeSpan TotalTime => TimeSpan.FromMilliseconds(TotalMsPlayed);
    public double TotalHours => TotalMsPlayed / 3_600_000d;
}

/// <summary>Listening time and play count for one playback-context bucket (device, country...).</summary>
public sealed class ContextStat
{
    public required string Name { get; init; }
    public long TotalMsPlayed { get; set; }
    public int PlayCount { get; set; }

    public TimeSpan TotalTime => TimeSpan.FromMilliseconds(TotalMsPlayed);
    public double TotalHours => TotalMsPlayed / 3_600_000d;
}

/// <summary>How often plays ended with a given Spotify "reason_end" value.</summary>
public sealed class ReasonEndStat
{
    public required string Reason { get; init; }
    public int Count { get; set; }
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

    // The same stats pre-sorted by play count, so switching a chart between "by time"
    // and "by plays" (or paging it while scrolling) never re-sorts on the UI thread.
    public IReadOnlyList<ArtistStat> ArtistsByPlayCount { get; init; } = Array.Empty<ArtistStat>();
    public IReadOnlyList<TrackStat> TracksByPlayCount { get; init; } = Array.Empty<TrackStat>();
    public IReadOnlyList<AlbumStat> AlbumsByPlayCount { get; init; } = Array.Empty<AlbumStat>();

    /// <summary>Per-calendar-year rollups, oldest year first.</summary>
    public IReadOnlyList<YearStat> Years { get; init; } = Array.Empty<YearStat>();

    /// <summary>
    /// Counts of Spotify "reason_end" values, most common first. Like the skip statistics,
    /// computed with the minimum-duration cutoff relaxed so skip-style endings show up.
    /// </summary>
    public IReadOnlyList<ReasonEndStat> ReasonEnds { get; init; } = Array.Empty<ReasonEndStat>();

    // ---- Podcasts and audiobooks ----------------------------------------------------------
    // Always aggregated, independent of FilterOptions.IncludePodcasts (which only controls
    // whether this content also counts toward the music track/artist/album statistics).

    /// <summary>Podcast shows and audiobooks, most listened first.</summary>
    public IReadOnlyList<ShowStat> Shows { get; init; } = Array.Empty<ShowStat>();

    /// <summary>Individual episodes and chapters, most listened first.</summary>
    public IReadOnlyList<EpisodeStat> Episodes { get; init; } = Array.Empty<EpisodeStat>();

    public long PodcastMsPlayed { get; init; }
    public int PodcastPlays { get; init; }

    // ---- Playback context -------------------------------------------------------------------

    /// <summary>Listening time grouped into device families (Desktop, Mobile, Web...).</summary>
    public IReadOnlyList<ContextStat> Platforms { get; init; } = Array.Empty<ContextStat>();

    /// <summary>Listening time grouped by the country the play streamed from.</summary>
    public IReadOnlyList<ContextStat> Countries { get; init; } = Array.Empty<ContextStat>();

    /// <summary>Plays that started in shuffle mode, out of <see cref="ShuffleEligiblePlays"/>.</summary>
    public int ShufflePlays { get; init; }

    /// <summary>Plays carrying a shuffle flag at all (older exports omit it).</summary>
    public int ShuffleEligiblePlays { get; init; }

    public int OfflinePlays { get; init; }
    public int IncognitoPlays { get; init; }

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

    /// <summary>Count of artists first heard in each calendar month (local time).</summary>
    public IReadOnlyList<DateTimePoint> NewArtistsByMonth { get; init; } = Array.Empty<DateTimePoint>();

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

    /// <summary>Run of consecutive listening days ending on the most recent listening day.</summary>
    public int CurrentStreakDays { get; init; }

    /// <summary>The single calendar day with the most listening time.</summary>
    public DateTime? BiggestDay { get; init; }
    public long BiggestDayMs { get; init; }

    // ---- Sessions --------------------------------------------------------------------------
    // A session is a run of plays where each play starts within AnalysisEngine.SessionGap
    // of the previous play's end.

    /// <summary>Number of distinct listening sessions.</summary>
    public int SessionCount { get; init; }

    /// <summary>Average listening time per session, in ms.</summary>
    public long AvgSessionMs { get; init; }

    /// <summary>Listening time of the single longest session, in ms.</summary>
    public long LongestSessionMs { get; init; }

    /// <summary>The day the longest session started.</summary>
    public DateTime? LongestSessionDate { get; init; }

    public TimeSpan TotalTime => TimeSpan.FromMilliseconds(TotalMsPlayed);

    public static AnalysisResult Empty { get; } = new();
}

/// <summary>A simple (date, value) pair for time-series charts.</summary>
public readonly record struct DateTimePoint(DateTime Date, double Value);
