using System.Globalization;

namespace Sortify.Models;

/// <summary>
/// The days a summary card covers, both ends included, and how the card words them: "MY YEAR
/// IN MUSIC" over "2024", or "MY 92 DAYS IN MUSIC" over "Jun 1 to Aug 31, 2024". The words are
/// English on any system, like the rest of the card.
/// </summary>
public sealed class CardRange
{
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    public CardRange(DateTime from, DateTime to, bool allTime = false)
    {
        From = from.Date;
        To = to.Date < From ? From : to.Date;
        AllTime = allTime;
    }

    public DateTime From { get; }
    public DateTime To { get; }

    /// <summary>True when no dates were picked, so the card covers the whole history.</summary>
    public bool AllTime { get; }

    public static CardRange ForYear(int year) => new(new DateTime(year, 1, 1), new DateTime(year, 12, 31));

    /// <summary>
    /// The range for the dates set in the sidebar, either of which may be open. An open end
    /// stops at the first or last play. Null with neither a date nor a play to go by.
    /// </summary>
    public static CardRange? For(DateTime? from, DateTime? to, DateTime? firstPlay, DateTime? lastPlay)
    {
        var start = from ?? firstPlay ?? to;
        var end = to ?? lastPlay ?? from;
        return start is { } s && end is { } e ? new CardRange(s, e, allTime: from is null && to is null) : null;
    }

    public int Days => (To - From).Days + 1;

    /// <summary>The calendar year the range is exactly, or null.</summary>
    public int? Year => !AllTime && CalendarYears() == 1 ? From.Year : null;

    /// <summary>True when the range sits inside one calendar year, so dates on the card can leave the year out.</summary>
    public bool WithinOneYear => From.Year == To.Year;

    /// <summary>The line above the heading.</summary>
    public string Kicker => AllTime ? "MY MUSIC, ALL TIME" : $"MY {Length().ToUpperInvariant()} IN MUSIC";

    /// <summary>The card's heading.</summary>
    public string Title
    {
        get
        {
            if (AllTime || CalendarYears() is not null)
                return WithinOneYear ? Num(From.Year) : $"{Num(From.Year)} to {Num(To.Year)}";
            if (CalendarMonths() is { } months)
                return months == 1 ? From.ToString("MMMM yyyy", En) : MonthSpan(" to ");
            return Days == 1 ? From.ToString("MMMM d, yyyy", En) : DaySpan(" to ");
        }
    }

    /// <summary>The range as a sentence refers to it: "in 2024", "between Jun 1 and Aug 31, 2024".</summary>
    public string Phrase
    {
        get
        {
            if (AllTime)
                return "in your history";
            if (CalendarYears() is not null)
                return WithinOneYear ? $"in {Num(From.Year)}" : $"between {Num(From.Year)} and {Num(To.Year)}";
            if (CalendarMonths() is { } months)
                return months == 1 ? $"in {From.ToString("MMMM yyyy", En)}" : $"between {MonthSpan(" and ")}";
            return Days == 1 ? $"on {From.ToString("MMMM d, yyyy", En)}" : $"between {DaySpan(" and ")}";
        }
    }

    /// <summary>A name for the saved image, without the extension.</summary>
    public string FileStem
    {
        get
        {
            if (AllTime)
                return "Sortify_summary_all_time";
            if (Year is { } year)
                return $"Sortify_{Num(year)}_in_review";
            if (CalendarMonths() == 1)
                return $"Sortify_summary_{From.ToString("yyyy-MM", En)}";
            string from = From.ToString("yyyy-MM-dd", En), to = To.ToString("yyyy-MM-dd", En);
            return Days == 1 ? $"Sortify_summary_{from}" : $"Sortify_summary_{from}_to_{to}";
        }
    }

    // "year", "3 years", "month", "week", "92 days". Whole years count wherever they start, so
    // the last 12 months of a history read as a year. Months only count from the 1st: a
    // rolling month is 30 or 31 days depending on where it falls, and the last 30 days should
    // read as 30 days whichever month they end in.
    private string Length()
    {
        if (WholeYears() is { } years)
            return years == 1 ? "year" : $"{Num(years)} years";
        if (CalendarMonths() is { } months)
            return months == 1 ? "month" : $"{Num(months)} months";
        return Days switch
        {
            1 => "day",
            7 => "week",
            _ => $"{Days.ToString("N0", En)} days",
        };
    }

    private int? CalendarYears() => From is { Month: 1, Day: 1 } ? WholeYears() : null;

    private int? CalendarMonths() => From.Day == 1 ? WholeMonths() : null;

    private int? WholeYears()
    {
        if (To >= DateTime.MaxValue.Date)
            return null;
        var after = To.AddDays(1);
        int years = after.Year - From.Year;
        return years >= 1 && From.AddYears(years) == after ? years : null;
    }

    private int? WholeMonths()
    {
        if (To >= DateTime.MaxValue.Date)
            return null;
        var after = To.AddDays(1);
        int months = (after.Year - From.Year) * 12 + after.Month - From.Month;
        return months >= 1 && From.AddMonths(months) == after ? months : null;
    }

    // "June to August 2024", "November 2023 to February 2024".
    private string MonthSpan(string joiner) => WithinOneYear
        ? $"{From.ToString("MMMM", En)}{joiner}{To.ToString("MMMM yyyy", En)}"
        : $"{From.ToString("MMMM yyyy", En)}{joiner}{To.ToString("MMMM yyyy", En)}";

    // "Jun 3 to 30, 2024", "Jun 3 to Aug 30, 2024", "Dec 20, 2023 to Jan 5, 2024".
    private string DaySpan(string joiner)
    {
        if (!WithinOneYear)
            return $"{From.ToString("MMM d, yyyy", En)}{joiner}{To.ToString("MMM d, yyyy", En)}";
        return From.Month == To.Month
            ? $"{From.ToString("MMM d", En)}{joiner}{To.ToString("d, yyyy", En)}"
            : $"{From.ToString("MMM d", En)}{joiner}{To.ToString("MMM d, yyyy", En)}";
    }

    private static string Num(int value) => value.ToString(En);
}
