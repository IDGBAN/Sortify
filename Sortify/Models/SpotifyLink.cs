namespace Sortify.Models;

/// <summary>A block of links to paste into a playlist, and how many tracks had none.</summary>
public readonly record struct PlaylistLinks(string Text, int Copied, int Missing);

/// <summary>Turns the Spotify URIs an export records into open.spotify.com links.</summary>
public static class SpotifyLink
{
    /// <summary>"spotify:track:abc" becomes "https://open.spotify.com/track/abc". Anything else gives an empty string.</summary>
    public static string FromUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri) || !uri.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var parts = uri.Split(':');
        // The URI comes straight out of a JSON file and ends up on a shell command line or the
        // clipboard, so its parts are escaped rather than trusted to be plain ids.
        if (parts.Length >= 3 && parts[1].Length > 0 && parts[2].Length > 0)
            return $"https://open.spotify.com/{Uri.EscapeDataString(parts[1])}/{Uri.EscapeDataString(parts[2])}";
        return string.Empty;
    }

    /// <summary>
    /// One link per line for every track that has one, in the order given and without
    /// repeats. The Spotify desktop app adds each line as a song when the block is pasted
    /// into a playlist.
    /// </summary>
    public static PlaylistLinks ForTracks(IEnumerable<TrackStat> tracks)
    {
        var links = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int missing = 0;
        foreach (var track in tracks)
        {
            string link = FromUri(track.Uri);
            if (link.Length == 0)
                missing++;
            else if (seen.Add(link))
                links.Add(link);
        }
        return new PlaylistLinks(string.Join("\n", links), links.Count, missing);
    }
}
