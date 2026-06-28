namespace Sortify.Services;

/// <summary>Shared formatting helpers for durations and timestamps.</summary>
public static class TimeFormat
{
    /// <summary>Formats a duration as total HH:MM:SS (hours can exceed 24), matching the original app.</summary>
    public static string HhMmSs(TimeSpan td)
    {
        long totalHours = (long)td.TotalHours;
        return $"{totalHours:00}:{td.Minutes:00}:{td.Seconds:00}";
    }

    /// <summary>Human-friendly duration like "12d 4h 30m" used in summaries.</summary>
    public static string Friendly(TimeSpan td)
    {
        if (td.TotalDays >= 1)
            return $"{(int)td.TotalDays}d {td.Hours}h {td.Minutes}m";
        if (td.TotalHours >= 1)
            return $"{(int)td.TotalHours}h {td.Minutes}m";
        return $"{td.Minutes}m {td.Seconds}s";
    }

    public static string Timestamp(DateTime dt) => dt.ToString("yyyy-MM-dd HH:mm");
}
