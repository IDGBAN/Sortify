using System.Collections.Concurrent;

namespace Sortify.Services;

/// <summary>
/// The device families plays are grouped into, from the platform string Spotify records for
/// each one. Those strings embed OS builds, device models and SDK versions, but a history only
/// holds a few dozen different ones, so each is classified once and remembered.
/// </summary>
public static class Devices
{
    private static readonly ConcurrentDictionary<string, string> Families = new(StringComparer.Ordinal);

    /// <summary>"Mobile", "Car", "Web player" and so on; empty for a play that records no platform.</summary>
    public static string FamilyOf(string platform) =>
        platform.Length == 0 ? string.Empty : Families.GetOrAdd(platform, Classify);

    /// <summary>
    /// Collapses a platform string into a handful of families worth charting. Matches whole
    /// tokens, so "tv" means a TV and not any word that happens to contain those letters.
    /// </summary>
    internal static string Classify(string platform)
    {
        string lower = platform.ToLowerInvariant();
        var tokens = lower.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);

        if (lower.Contains("web_player") || lower.Contains("webplayer"))
            return "Web player";

        // TVs, consoles and cars report as "Partner <device> ...", so they have to be matched
        // before the catch-all partner token sends them to the speaker bucket.
        if (lower.Contains("android_auto") || lower.Contains("android auto") || tokens.Any(CarTokens.Contains))
            return "Car";
        if (tokens.Any(TvTokens.Contains))
            return "TV / console";
        if (tokens.Any(SpeakerTokens.Contains))
            return "Speaker / cast";
        if (tokens.Any(MobileTokens.Contains))
            return "Mobile";

        // Spotify writes macOS as "OS X 10.15.7 [x86_64]", with a space.
        if (tokens.Any(DesktopTokens.Contains) || lower.Contains("os x"))
            return "Desktop";
        return "Other";
    }

    private static readonly char[] TokenSeparators =
        " _-;:,.()[]/\\".ToCharArray();

    private static readonly HashSet<string> CarTokens = new(StringComparer.Ordinal)
    {
        "car", "carplay", "automotive", "androidauto",
    };

    private static readonly HashSet<string> TvTokens = new(StringComparer.Ordinal)
    {
        "tv", "smarttv", "androidtv", "appletv", "firetv", "googletv", "tizen", "webos", "roku", "bravia",
        "xbox", "playstation", "ps3", "ps4", "ps5",
    };

    private static readonly HashSet<string> SpeakerTokens = new(StringComparer.Ordinal)
    {
        "cast", "chromecast", "sonos", "speaker", "echo", "alexa", "homepod", "bose", "partner",
    };

    private static readonly HashSet<string> MobileTokens = new(StringComparer.Ordinal)
    {
        "android", "ios", "iphone", "ipad", "ipod",
    };

    private static readonly HashSet<string> DesktopTokens = new(StringComparer.Ordinal)
    {
        "windows", "osx", "macos", "mac", "linux",
    };
}
