namespace Sortify.Models;

/// <summary>How a play's shuffle, offline or private-session flag decides whether it counts.</summary>
public enum PlaybackMode
{
    /// <summary>The flag doesn't matter.</summary>
    Any,

    /// <summary>Only plays known to have the flag set.</summary>
    Only,

    /// <summary>Plays with the flag set are left out. Plays that don't record it stay in.</summary>
    Exclude,
}

/// <summary>
/// User-customizable filters applied to raw play records before aggregation.
/// </summary>
public sealed class FilterOptions
{
    /// <summary>Default minimum play duration: drop listens under 5 seconds.</summary>
    public const int DefaultMinMs = 5000;

    /// <summary>Minimum ms_played for a record to be counted.</summary>
    public int MinMsPlayed { get; set; } = DefaultMinMs;

    /// <summary>
    /// When false (the default) only music plays feed the track/artist/album statistics,
    /// so podcasts and audiobooks can't distort them. The Podcasts tab reads its own
    /// aggregates and is unaffected by this.
    /// </summary>
    public bool IncludePodcasts { get; set; }

    /// <summary>Inclusive start of the date range (local time, matching record timestamps). Null = no lower bound.</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>Inclusive end of the date range (local time, matching record timestamps). Null = no upper bound.</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Case-insensitive substring matched against track, artist or album name (show or
    /// episode name for podcasts and audiobooks). Empty = no search filter.
    /// </summary>
    public string SearchTerm { get; set; } = string.Empty;

    /// <summary>Artist names to exclude entirely (case-insensitive). Matches a podcast's show name.</summary>
    public HashSet<string> ExcludedArtists { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Track names to exclude entirely (case-insensitive). Matches a podcast's episode name.</summary>
    public HashSet<string> ExcludedTracks { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Inclusive start hour of day (0-23).</summary>
    public int StartHour { get; set; } = 0;

    /// <summary>Inclusive end hour of day (0-23). May be less than StartHour to wrap past midnight.</summary>
    public int EndHour { get; set; } = 23;

    /// <summary>
    /// Days of the week that are included. Index 0=Sunday..6=Saturday.
    /// All true by default.
    /// </summary>
    public bool[] IncludedDaysOfWeek { get; } = { true, true, true, true, true, true, true };

    public PlaybackMode Shuffle { get; set; }
    public PlaybackMode Offline { get; set; }
    public PlaybackMode Private { get; set; }

    /// <summary>
    /// Device families to leave out, as <c>AnalysisEngine.PlatformFamily</c> names them
    /// ("Mobile", "Car"...). Plays that don't record a device are never left out by this.
    /// </summary>
    public HashSet<string> ExcludedDevices { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Two-letter countries to leave out. Plays with no country are never left out by this.</summary>
    public HashSet<string> ExcludedCountries { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasAllDaysSelected => IncludedDaysOfWeek.All(d => d);

    /// <summary>A copy with a different date range and every other filter the same.</summary>
    public FilterOptions WithDateRange(DateTime? start, DateTime? end)
    {
        var copy = new FilterOptions
        {
            MinMsPlayed = MinMsPlayed,
            IncludePodcasts = IncludePodcasts,
            StartDate = start,
            EndDate = end,
            SearchTerm = SearchTerm,
            StartHour = StartHour,
            EndHour = EndHour,
            Shuffle = Shuffle,
            Offline = Offline,
            Private = Private,
        };
        copy.ExcludedArtists.UnionWith(ExcludedArtists);
        copy.ExcludedTracks.UnionWith(ExcludedTracks);
        copy.ExcludedDevices.UnionWith(ExcludedDevices);
        copy.ExcludedCountries.UnionWith(ExcludedCountries);
        IncludedDaysOfWeek.CopyTo(copy.IncludedDaysOfWeek, 0);
        return copy;
    }

    public bool HasFullHourRange => StartHour == 0 && EndHour == 23;

    /// <summary>True when an hour falls within the configured range, supporting ranges that wrap past midnight.</summary>
    public bool HourMatches(int hour)
    {
        if (HasFullHourRange) return true;
        if (StartHour <= EndHour)
            return hour >= StartHour && hour <= EndHour;
        // Wrapping range, e.g. 22 -> 2.
        return hour >= StartHour || hour <= EndHour;
    }
}
