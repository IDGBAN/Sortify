namespace Sortify.Models;

/// <summary>
/// User-customizable filters applied to raw play records before aggregation.
/// </summary>
public sealed class FilterOptions
{
    /// <summary>Default minimum play duration: drop listens under 5 seconds.</summary>
    public const int DefaultMinMs = 5000;

    /// <summary>Minimum ms_played for a record to be counted.</summary>
    public int MinMsPlayed { get; set; } = DefaultMinMs;

    /// <summary>Inclusive start of the date range (UTC). Null = no lower bound.</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>Inclusive end of the date range (UTC). Null = no upper bound.</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>Case-insensitive substring matched against track or artist name. Empty = no search filter.</summary>
    public string SearchTerm { get; set; } = string.Empty;

    /// <summary>Artist names to exclude entirely (case-insensitive).</summary>
    public HashSet<string> ExcludedArtists { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Track names to exclude entirely (case-insensitive).</summary>
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

    public bool HasAllDaysSelected => IncludedDaysOfWeek.All(d => d);

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
