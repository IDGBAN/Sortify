namespace Sortify.Services;

/// <summary>Week arithmetic for a chosen first day of the week.</summary>
public static class Weeks
{
    /// <summary>The days a week can be set to start on: Sunday, Monday or Saturday, as calendars around the world do.</summary>
    public static readonly DayOfWeek[] Starts = { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Saturday };

    /// <summary>The first day of the week that <paramref name="date"/> falls in.</summary>
    public static DateTime Start(DateTime date, DayOfWeek first) =>
        date.Date.AddDays(-(((int)date.DayOfWeek - (int)first + 7) % 7));

    /// <summary>The seven days in order, starting with <paramref name="first"/>.</summary>
    public static DayOfWeek[] Ordered(DayOfWeek first) =>
        Enumerable.Range(0, 7).Select(i => (DayOfWeek)(((int)first + i) % 7)).ToArray();
}
