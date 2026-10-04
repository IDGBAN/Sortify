using CommunityToolkit.Mvvm.ComponentModel;
using Sortify.Services;

namespace Sortify.ViewModels;

/// <summary>A one-click date range: all time, a recent stretch, or a calendar year.</summary>
public sealed partial class DateRangeOption : ObservableObject
{
    /// <summary>Length of the shorter "recent" range, in days.</summary>
    public const int RecentDays = 30;

    /// <summary>Length of the longer "recent" range, in months.</summary>
    public const int RecentMonths = 12;

    public DateRangeOption(string label, DateTime? start, DateTime? end, string tooltip)
    {
        Label = label;
        Start = start;
        End = end;
        Tooltip = tooltip;
    }

    public string Label { get; }

    /// <summary>First day of the range, or null for no lower bound.</summary>
    public DateTime? Start { get; }

    /// <summary>Last day of the range (inclusive), or null for no upper bound.</summary>
    public DateTime? End { get; }

    public string Tooltip { get; }

    /// <summary>True while the filter's dates are exactly this range.</summary>
    [ObservableProperty] private bool _isActive;

    public bool Matches(DateTime? start, DateTime? end) => Start == start?.Date && End == end?.Date;

    /// <summary>
    /// The ranges worth offering for a history whose last play was on <paramref name="lastListen"/>.
    /// The recent ranges count back from that day rather than from today: an export is a
    /// snapshot, often weeks old, and "the last 30 days" of it would otherwise be empty.
    /// </summary>
    public static IReadOnlyList<DateRangeOption> For(DateTime? lastListen, IEnumerable<int> years)
    {
        if (lastListen is not { } last)
            return Array.Empty<DateRangeOption>();

        var end = last.Date;
        var recentStart = end.AddDays(-(RecentDays - 1));
        var yearStart = end.AddMonths(-RecentMonths).AddDays(1);

        var options = new List<DateRangeOption>
        {
            new("All time", null, null, "Every play in the export"),
            new($"Last {RecentDays} days", recentStart, end, $"{TimeFormat.Day(recentStart)} to {TimeFormat.Day(end)}, the end of your history"),
            new($"Last {RecentMonths} months", yearStart, end, $"{TimeFormat.Day(yearStart)} to {TimeFormat.Day(end)}, the end of your history"),
        };
        foreach (int year in years.Distinct().OrderBy(y => y))
            options.Add(new(year.ToString(), new DateTime(year, 1, 1), new DateTime(year, 12, 31), $"All of {year}"));
        return options;
    }
}
