using System.Globalization;

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

    /// <summary>
    /// Invariant because ':' in a custom format is the culture's time separator, and these
    /// strings end up in CSV files that other tools parse.
    /// </summary>
    public static string Timestamp(DateTime dt) => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Null-tolerant variant used by exports; empty string when no timestamp exists.</summary>
    public static string Timestamp(DateTime? dt) => dt is { } d ? Timestamp(d) : string.Empty;

    /// <summary>
    /// "2024-03-14". Invariant like <see cref="Timestamp(DateTime)"/>: under the current culture a
    /// Thai or Saudi system would write the year as 2567 or 1445.
    /// </summary>
    public static string Day(DateTime dt) => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>"2024-03", for month buckets.</summary>
    public static string Month(DateTime dt) => dt.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
