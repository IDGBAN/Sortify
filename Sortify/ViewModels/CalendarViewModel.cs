using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sortify.Models;
using Sortify.Services;

namespace Sortify.ViewModels;

/// <summary>
/// The listening calendar on the Trends tab: one square per day of a year, shaded by how long
/// was listened, a column per week.
/// </summary>
public sealed partial class CalendarViewModel : ObservableObject
{
    /// <summary>
    /// Width of one week's column plus the gap after it, in device-independent pixels. The
    /// month labels are placed by it, so it has to match the squares' size and margin in XAML.
    /// </summary>
    public const double ColumnPitch = 16;

    /// <summary>Listening per day, in hours, under the current filters.</summary>
    private Dictionary<DateTime, double> _hours = new();

    private DayOfWeek _firstDay = DayOfWeek.Sunday;

    /// <summary>The years the history has listening in, oldest first.</summary>
    [ObservableProperty] private IReadOnlyList<CalendarYear> _years = Array.Empty<CalendarYear>();

    [ObservableProperty] private int _year;

    /// <summary>The year's weeks, each with its seven days from the chosen first day.</summary>
    [ObservableProperty] private IReadOnlyList<CalendarWeek> _weeks = Array.Empty<CalendarWeek>();

    [ObservableProperty] private IReadOnlyList<CalendarMonthLabel> _monthLabels = Array.Empty<CalendarMonthLabel>();

    /// <summary>The short day names down the side, in week order, with only every other one shown.</summary>
    [ObservableProperty] private IReadOnlyList<string> _dayLabels = Array.Empty<string>();

    [ObservableProperty] private string _summary = string.Empty;

    [ObservableProperty] private bool _hasDays;

    /// <summary>
    /// Takes the listening of a new analysis. The year on show stays when it still has
    /// listening; otherwise the calendar moves to the latest year that does.
    /// </summary>
    public void Update(IReadOnlyList<DateTimePoint> playtimeByDay, DayOfWeek firstDay)
    {
        _hours = playtimeByDay.ToDictionary(p => p.Date.Date, p => p.Value);
        _firstDay = firstDay;

        var years = _hours.Keys.Select(d => d.Year).Distinct().Order().ToList();
        HasDays = years.Count > 0;
        int year = years.Contains(Year) ? Year : years.Count > 0 ? years[^1] : 0;
        Years = years.Select(y => new CalendarYear(y) { IsActive = y == year }).ToList();
        Show(year);
    }

    [RelayCommand]
    private void ShowYear(CalendarYear? option)
    {
        if (option is null)
            return;
        foreach (var y in Years)
            y.IsActive = y.Year == option.Year;
        Show(option.Year);
    }

    private void Show(int year)
    {
        Year = year;
        if (year == 0)
        {
            Weeks = Array.Empty<CalendarWeek>();
            MonthLabels = Array.Empty<CalendarMonthLabel>();
            DayLabels = Array.Empty<string>();
            Summary = "No dated plays match the current filters.";
            return;
        }

        var first = new DateTime(year, 1, 1);
        var last = new DateTime(year, 12, 31);
        var levels = Levels(year);

        var weeks = new List<CalendarWeek>();
        var months = new List<CalendarMonthLabel>();
        for (var weekStart = Services.Weeks.Start(first, _firstDay); weekStart <= last; weekStart = weekStart.AddDays(7))
        {
            var days = new CalendarDay[7];
            for (int i = 0; i < 7; i++)
            {
                var date = weekStart.AddDays(i);
                if (date.Year != year)
                {
                    days[i] = CalendarDay.OutsideYear(date);
                    continue;
                }

                double hours = _hours.GetValueOrDefault(date);
                days[i] = new CalendarDay(date, hours, levels(hours));
                if (date.Day == 1)
                    months.Add(new CalendarMonthLabel(date.ToString("MMM", System.Globalization.CultureInfo.InvariantCulture), weeks.Count * ColumnPitch));
            }
            weeks.Add(new CalendarWeek(days));
        }

        Weeks = weeks;
        MonthLabels = months;
        DayLabels = Services.Weeks.Ordered(_firstDay)
            .Select((d, i) => i % 2 == 1 ? d.ToString()[..3] : string.Empty)
            .ToList();

        var active = weeks.SelectMany(w => w.Days).Where(d => d.HasListening).ToList();
        var busiest = active.MaxBy(d => d.Hours);
        int daysInYear = DateTime.IsLeapYear(year) ? 366 : 365;
        Summary = busiest is null
            ? $"Nothing was played in {year} under the current filters."
            : $"Listening on {active.Count:N0} of {daysInYear} days in {year}. The busiest was {busiest.DateText}, with {busiest.TimeText}.";
    }

    /// <summary>
    /// The shade for a day's listening: none, then quarters of the year's listening days, so a
    /// light listener's calendar is as readable as a heavy one's.
    /// </summary>
    private Func<double, int> Levels(int year)
    {
        var sorted = _hours.Where(kv => kv.Key.Year == year && kv.Value > 0).Select(kv => kv.Value).Order().ToArray();
        if (sorted.Length == 0)
            return _ => 0;

        double q1 = Quantile(sorted, 0.25), q2 = Quantile(sorted, 0.5), q3 = Quantile(sorted, 0.75);
        return hours => hours <= 0 ? 0 : hours <= q1 ? 1 : hours <= q2 ? 2 : hours <= q3 ? 3 : 4;
    }

    private static double Quantile(double[] sorted, double q) => sorted[(int)Math.Floor(q * (sorted.Length - 1))];
}

/// <summary>A year the calendar can show.</summary>
public sealed partial class CalendarYear : ObservableObject
{
    public CalendarYear(int year) => Year = year;

    public int Year { get; }

    /// <summary>True while it is the year on show.</summary>
    [ObservableProperty] private bool _isActive;

    public override string ToString() => Year.ToString();
}

/// <summary>A column of the calendar: one week.</summary>
public sealed record CalendarWeek(IReadOnlyList<CalendarDay> Days);

/// <summary>A month's name above the column its first day falls in.</summary>
public sealed record CalendarMonthLabel(string Text, double Left);

/// <summary>One square of the calendar.</summary>
public sealed class CalendarDay
{
    public CalendarDay(DateTime date, double hours, int level)
    {
        Date = date.Date;
        Hours = hours;
        Level = level;
    }

    /// <summary>A square that pads the first or last week out to seven days.</summary>
    internal static CalendarDay OutsideYear(DateTime date) => new(date, 0, 0) { IsInYear = false };

    public DateTime Date { get; }

    /// <summary>Hours listened that day.</summary>
    public double Hours { get; }

    /// <summary>0 for no listening, then 1 to 4 from the lightest quarter of days to the heaviest.</summary>
    public int Level { get; }

    public bool IsInYear { get; private init; } = true;

    public bool HasListening => IsInYear && Hours > 0;

    public string DateText => TimeFormat.Day(Date);

    public string TimeText => TimeFormat.Friendly(TimeSpan.FromHours(Hours));

    /// <summary>What the square says when hovered or read out.</summary>
    public string Description => !IsInYear
        ? string.Empty
        : $"{Date.DayOfWeek} {DateText}: {(Hours > 0 ? TimeText : "nothing played")}";
}
