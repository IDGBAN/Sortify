using System.Globalization;

namespace Sortify.Models;

/// <summary>One line in a summary card's top five.</summary>
public sealed record YearReviewEntry(int Rank, string Name, string Secondary, string Time);

/// <summary>One of the figures in the row under the minutes: "106" over "artists".</summary>
public sealed record YearReviewStat(string Value, string Label);

/// <summary>Everything on a shareable summary card, for a calendar year or any other stretch of the history.</summary>
public sealed class YearReview
{
    /// <summary>The days the card covers, and how it words them.</summary>
    public required CardRange Range { get; init; }

    /// <summary>The calendar year the card covers, or null for any other stretch.</summary>
    public int? Year => Range.Year;

    public long TotalMsPlayed { get; init; }
    public int Plays { get; init; }
    public int Artists { get; init; }
    public int Tracks { get; init; }

    /// <summary>Artists heard for the very first time within the range, judged against the whole history.</summary>
    public int NewArtists { get; init; }

    public int LongestStreakDays { get; init; }

    /// <summary>
    /// The figures in the row under the minutes, picked to suit the range: an all-time card has
    /// no new artists to count, and a single day has no streak.
    /// </summary>
    public IReadOnlyList<YearReviewStat> Stats { get; init; } = Array.Empty<YearReviewStat>();

    public IReadOnlyList<YearReviewEntry> TopArtists { get; init; } = Array.Empty<YearReviewEntry>();
    public IReadOnlyList<YearReviewEntry> TopTracks { get; init; } = Array.Empty<YearReviewEntry>();

    /// <summary>First day of the month with the most listening.</summary>
    public DateTime? BiggestMonth { get; init; }
    public long BiggestMonthMs { get; init; }
    public DateTime? BiggestDay { get; init; }
    public long BiggestDayMs { get; init; }

    /// <summary>"March, 85 hours", or empty when the range is too short for its months to say much.</summary>
    public string BiggestMonthText { get; init; } = string.Empty;

    /// <summary>"March 4 to 10, 12 hours", for a range too short to name a month but long enough to have weeks. Otherwise empty.</summary>
    public string BiggestWeekText { get; init; } = string.Empty;

    /// <summary>"March 14, 9h 12m", or empty for a single day or with no dated plays.</summary>
    public string BiggestDayText { get; init; } = string.Empty;

    /// <summary>The sidebar filters other than dates, so a filtered card says it is one. Empty when none.</summary>
    public string FilterNote { get; init; } = string.Empty;

    public long TotalMinutes => TotalMsPlayed / 60_000;
    public double TotalHours => TotalMsPlayed / 3_600_000d;

    public string MinutesWord => TotalMinutes == 1 ? "minute" : "minutes";

    /// <summary>"555 hours of listening", or empty under an hour, where the minutes say it all.</summary>
    public string HoursText
    {
        get
        {
            if (TotalHours < 1)
                return string.Empty;
            string hours = TotalHours.ToString(TotalHours >= 10 ? "N0" : "0.#", CultureInfo.InvariantCulture);
            return hours == "1" ? "1 hour of listening" : $"{hours} hours of listening";
        }
    }
}
