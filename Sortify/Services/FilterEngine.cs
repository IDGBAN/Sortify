using Sortify.Models;

namespace Sortify.Services;

/// <summary>Applies user-configured FilterOptions to a sequence of raw play records.</summary>
public static class FilterEngine
{
    public static IEnumerable<PlayRecord> Apply(IEnumerable<PlayRecord> records, FilterOptions filter)
    {
        bool hasSearch = !string.IsNullOrWhiteSpace(filter.SearchTerm);
        string search = filter.SearchTerm.Trim();

        foreach (var r in records)
        {
            if (r.MsPlayed < filter.MinMsPlayed)
                continue;

            if (filter.StartDate is { } start && r.Timestamp < start)
                continue;

            if (filter.EndDate is { } end && r.Timestamp > end)
                continue;

            if (filter.ExcludedArtists.Contains(r.ArtistName))
                continue;

            if (filter.ExcludedTracks.Contains(r.TrackName))
                continue;

            // Time-of-day / day-of-week only apply when a real timestamp exists.
            if (r.Timestamp != DateTime.MinValue)
            {
                if (!filter.HourMatches(r.Timestamp.Hour))
                    continue;

                if (!filter.HasAllDaysSelected && !filter.IncludedDaysOfWeek[(int)r.Timestamp.DayOfWeek])
                    continue;
            }

            if (hasSearch &&
                r.TrackName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                r.ArtistName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            yield return r;
        }
    }
}
