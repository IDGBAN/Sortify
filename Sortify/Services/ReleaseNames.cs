using Sortify.Models;

namespace Sortify.Services;

/// <summary>
/// Folds re-releases of a recording into the original: "Song - 2011 Remaster" into "Song",
/// "Album (Deluxe Edition)" into "Album". Only a tag at the end of a name that is all about
/// the edition goes; live versions, remixes, radio edits and the like are other recordings
/// and keep their names, as do featured-artist credits.
/// </summary>
public static class ReleaseNames
{
    /// <summary>
    /// The words an edition tag is made of. Numbers ("2011") and ordinals ("50th") count as
    /// well. A tag with any other word in it, like "Live" or "Taylor's Version", is left alone.
    /// </summary>
    public static readonly IReadOnlySet<string> TagWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "remaster", "remastered", "digital", "digitally", "deluxe", "super", "expanded", "edition", "version",
        "anniversary", "year", "bonus", "track", "tracks", "mono", "stereo", "single", "album", "the", "and",
    };

    /// <summary>
    /// At least one of these has to be in a tag for it to go; "Single Version" and "Album
    /// Version" count too. Otherwise a tag like "Edition" or "2011" alone would be stripped.
    /// </summary>
    public static readonly IReadOnlySet<string> KeyWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "remaster", "remastered", "deluxe", "expanded", "anniversary", "bonus", "mono", "stereo",
    };

    private static readonly char[] TagSeparators = { ' ', '/', '&', '+', ',' };

    /// <summary>The name without its edition tags, or the very same string when it has none.</summary>
    public static string Original(string name)
    {
        string current = name;
        while (StripLastTag(current) is { } shorter)
            current = shorter;
        return current.Length == 0 ? name : current;
    }

    /// <summary>
    /// The plays with each track and album under its original name. Plays whose names don't
    /// change are the same objects; only the renamed ones are copied. Podcasts and audiobooks
    /// are left as they are.
    /// </summary>
    public static List<PlayRecord> Combine(IReadOnlyList<PlayRecord> records)
    {
        // Names repeat across thousands of plays, so each is worked out once.
        var originals = new Dictionary<string, string>(StringComparer.Ordinal);
        string OriginalOf(string name)
        {
            if (!originals.TryGetValue(name, out var original))
                originals[name] = original = Original(name);
            return original;
        }

        var combined = new List<PlayRecord>(records.Count);
        foreach (var record in records)
        {
            if (record.Kind != ContentKind.Music)
            {
                combined.Add(record);
                continue;
            }

            string track = OriginalOf(record.TrackName);
            string album = OriginalOf(record.AlbumName);
            combined.Add(ReferenceEquals(track, record.TrackName) && ReferenceEquals(album, record.AlbumName)
                ? record
                : record.WithNames(track, album));
        }
        return combined;
    }

    /// <summary>The name without its last tag: "(...)", "[...]" or " - ..." at the end. Null when that isn't an edition tag.</summary>
    private static string? StripLastTag(string name)
    {
        string trimmed = name.TrimEnd();
        if (trimmed.Length == 0)
            return null;

        char last = trimmed[^1];
        if (last is ')' or ']')
        {
            int open = trimmed.LastIndexOf(last == ')' ? '(' : '[');
            return open > 0 && IsEditionTag(trimmed[(open + 1)..^1]) ? trimmed[..open].TrimEnd() : null;
        }

        // Spotify separates a version from the title with a spaced hyphen; an en dash turns up too.
        int dash = Math.Max(trimmed.LastIndexOf(" - ", StringComparison.Ordinal), trimmed.LastIndexOf(" – ", StringComparison.Ordinal));
        return dash > 0 && IsEditionTag(trimmed[(dash + 3)..]) ? trimmed[..dash].TrimEnd() : null;
    }

    private static bool IsEditionTag(string tag)
    {
        var words = tag.Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return false;

        bool key = false, version = false, singleOrAlbum = false;
        foreach (var word in words)
        {
            if (IsNumber(word))
                continue;
            if (!TagWords.Contains(word))
                return false;
            key |= KeyWords.Contains(word);
            version |= word.Equals("version", StringComparison.OrdinalIgnoreCase);
            singleOrAlbum |= word.Equals("single", StringComparison.OrdinalIgnoreCase) || word.Equals("album", StringComparison.OrdinalIgnoreCase);
        }
        return key || (version && singleOrAlbum);
    }

    // "2011", "50th", "1st".
    private static bool IsNumber(string word)
    {
        int digits = 0;
        while (digits < word.Length && char.IsAsciiDigit(word[digits]))
            digits++;
        if (digits == 0)
            return false;
        string rest = word[digits..];
        return rest.Length == 0 || rest.Equals("st", StringComparison.OrdinalIgnoreCase) || rest.Equals("nd", StringComparison.OrdinalIgnoreCase)
            || rest.Equals("rd", StringComparison.OrdinalIgnoreCase) || rest.Equals("th", StringComparison.OrdinalIgnoreCase);
    }
}
