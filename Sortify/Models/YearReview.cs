namespace Sortify.Models;

/// <summary>One line in a year-in-review top five.</summary>
public sealed record YearReviewEntry(int Rank, string Name, string Secondary, string Time);

/// <summary>Everything on a year's shareable summary card.</summary>
public sealed class YearReview
{
    public required int Year { get; init; }
    public long TotalMsPlayed { get; init; }
    public int Plays { get; init; }
    public int Artists { get; init; }
    public int Tracks { get; init; }

    /// <summary>Artists heard for the very first time this year, judged against the whole history.</summary>
    public int NewArtists { get; init; }

    public int LongestStreakDays { get; init; }
    public IReadOnlyList<YearReviewEntry> TopArtists { get; init; } = Array.Empty<YearReviewEntry>();
    public IReadOnlyList<YearReviewEntry> TopTracks { get; init; } = Array.Empty<YearReviewEntry>();

    /// <summary>First day of the month with the most listening.</summary>
    public DateTime? BiggestMonth { get; init; }
    public long BiggestMonthMs { get; init; }
    public DateTime? BiggestDay { get; init; }
    public long BiggestDayMs { get; init; }

    /// <summary>"March, 85 hours", or empty with no dated plays.</summary>
    public string BiggestMonthText { get; init; } = string.Empty;

    /// <summary>"March 14, 9h 12m", or empty with no dated plays.</summary>
    public string BiggestDayText { get; init; } = string.Empty;

    /// <summary>The sidebar filters other than dates, so a filtered card says it is one. Empty when none.</summary>
    public string FilterNote { get; init; } = string.Empty;

    public long TotalMinutes => TotalMsPlayed / 60_000;
    public double TotalHours => TotalMsPlayed / 3_600_000d;
}
