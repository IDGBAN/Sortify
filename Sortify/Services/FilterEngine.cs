using Sortify.Models;

namespace Sortify.Services;

/// <summary>Applies user-configured FilterOptions to a sequence of raw play records.</summary>
public static class FilterEngine
{
    /// <param name="ignoreMinDuration">
    /// When true the minimum-duration cutoff is skipped, so short (usually skipped) plays
    /// still come through. Used by the skip-rate statistics.
    /// </param>
    public static IEnumerable<PlayRecord> Apply(
        IEnumerable<PlayRecord> records, FilterOptions filter, bool ignoreMinDuration = false)
    {
        // Hoist per-filter invariants out of the per-record loop; HasAllDaysSelected in
        // particular walks the day array, which adds up over hundreds of thousands of rows.
        bool hasSearch = !string.IsNullOrWhiteSpace(filter.SearchTerm);
        string search = filter.SearchTerm.Trim();
        bool allDays = filter.HasAllDaysSelected;
        bool fullHours = filter.HasFullHourRange;
        bool hasExcludedArtists = filter.ExcludedArtists.Count > 0;
        bool hasExcludedTracks = filter.ExcludedTracks.Count > 0;
        bool checkTimeOfDay = !fullHours || !allDays;

        foreach (var r in records)
        {
            if (!ignoreMinDuration && r.MsPlayed < filter.MinMsPlayed)
                continue;

            if (filter.StartDate is { } start && r.Timestamp < start)
                continue;

            if (filter.EndDate is { } end && r.Timestamp > end)
                continue;

            if (hasExcludedArtists && filter.ExcludedArtists.Contains(r.ArtistName))
                continue;

            if (hasExcludedTracks && filter.ExcludedTracks.Contains(r.TrackName))
                continue;

            // Time-of-day / day-of-week only apply when a real timestamp exists.
            if (checkTimeOfDay && r.Timestamp != DateTime.MinValue)
            {
                if (!fullHours && !filter.HourMatches(r.Timestamp.Hour))
                    continue;

                if (!allDays && !filter.IncludedDaysOfWeek[(int)r.Timestamp.DayOfWeek])
                    continue;
            }

            if (hasSearch && !Matches(r, search))
                continue;

            yield return r;
        }
    }

    /// <summary>
    /// Searches the fields that carry a name for this record's kind: track/artist/album for
    /// music, show/episode (or audiobook/chapter) titles otherwise.
    /// </summary>
    private static bool Matches(PlayRecord r, string search)
    {
        if (r.Kind == ContentKind.Music)
        {
            return r.TrackName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || r.ArtistName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || r.AlbumName.Contains(search, StringComparison.OrdinalIgnoreCase);
        }

        return r.ShowName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || r.EpisodeName.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}
