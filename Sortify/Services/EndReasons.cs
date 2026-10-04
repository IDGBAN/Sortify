namespace Sortify.Services;

/// <summary>Readable names for Spotify's "reason_end" codes.</summary>
public static class EndReasons
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trackdone"] = "Track finished",
        ["fwdbtn"] = "Skipped (next)",
        ["backbtn"] = "Back button",
        ["endplay"] = "Playback stopped",
        ["logout"] = "Logged out",
        ["trackerror"] = "Track error",
        ["remote"] = "Remote control",
        ["appload"] = "App reloaded",
        ["popup"] = "Popup",
        ["uriopen"] = "Opened elsewhere",
        ["clickrow"] = "Picked another track",
        ["playbtn"] = "Play button",
        ["clickside"] = "Sidebar click",
        ["unexpected-exit"] = "App exited",
        ["unexpected-exit-while-paused"] = "Exited while paused",
        ["unknown"] = "Unknown",
    };

    /// <summary>The readable name for a code, or the code itself when Spotify has added one since.</summary>
    public static string Label(string reason) => Labels.TryGetValue(reason, out var label) ? label : reason;
}
