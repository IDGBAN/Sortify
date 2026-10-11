namespace Sortify.Models;

/// <summary>
/// What the charts and lists rank and measure by: how long something was listened to, or how
/// many times it was played. Chosen with the Time / Plays switch beside the main window's tabs.
/// </summary>
public enum Metric
{
    /// <summary>Listening time.</summary>
    Time,

    /// <summary>Number of plays.</summary>
    Plays,
}
