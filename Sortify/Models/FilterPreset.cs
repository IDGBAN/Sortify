namespace Sortify.Models;

/// <summary>
/// Every filter in the sidebar, as plain values that can be written to the settings file.
/// Used both for the named presets the user saves and for the filters remembered between
/// launches.
/// </summary>
public sealed class FilterPreset
{
    public string Name { get; set; } = string.Empty;

    public int MinSeconds { get; set; } = FilterOptions.DefaultMinMs / 1000;
    public bool IncludePodcasts { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string SearchTerm { get; set; } = string.Empty;
    public int StartHour { get; set; }
    public int EndHour { get; set; } = 23;

    /// <summary>Index 0=Sunday..6=Saturday, as in <see cref="FilterOptions.IncludedDaysOfWeek"/>.</summary>
    public bool[] IncludedDays { get; set; } = { true, true, true, true, true, true, true };

    public List<string> ExcludedArtists { get; set; } = new();
    public List<string> ExcludedTracks { get; set; } = new();

    /// <summary>
    /// Repairs what a hand-edited or older settings file could hold: nulls, blanks, hours out
    /// of range or a day list of the wrong length.
    /// </summary>
    public void Normalize()
    {
        Name = Name?.Trim() ?? string.Empty;
        MinSeconds = Math.Max(0, MinSeconds);
        SearchTerm ??= string.Empty;
        StartHour = Math.Clamp(StartHour, 0, 23);
        EndHour = Math.Clamp(EndHour, 0, 23);
        if (IncludedDays is not { Length: 7 })
            IncludedDays = new[] { true, true, true, true, true, true, true };
        ExcludedArtists = Clean(ExcludedArtists);
        ExcludedTracks = Clean(ExcludedTracks);
    }

    private static List<string> Clean(List<string>? names) => (names ?? new List<string>())
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Select(n => n.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>What a screen reader announces for the preset in the sidebar list.</summary>
    public override string ToString() => Name;

    /// <summary>True when both hold the same filters. The name is not compared.</summary>
    public bool SameFiltersAs(FilterPreset other) =>
        MinSeconds == other.MinSeconds &&
        IncludePodcasts == other.IncludePodcasts &&
        StartDate == other.StartDate &&
        EndDate == other.EndDate &&
        SearchTerm == other.SearchTerm &&
        StartHour == other.StartHour &&
        EndHour == other.EndHour &&
        IncludedDays.SequenceEqual(other.IncludedDays) &&
        ExcludedArtists.SequenceEqual(other.ExcludedArtists) &&
        ExcludedTracks.SequenceEqual(other.ExcludedTracks);
}
