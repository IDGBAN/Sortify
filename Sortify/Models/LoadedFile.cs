namespace Sortify.Models;

/// <summary>What a history file turned out to be.</summary>
public enum LoadedFileFormat
{
    /// <summary>The extended streaming history, with everything Spotify keeps about a play.</summary>
    Extended,

    /// <summary>The older account-data export: about a year of plays with only the track, artist and end time.</summary>
    AccountData,

    /// <summary>Not read: empty, broken, or not a listening history at all.</summary>
    Skipped,
}

/// <summary>
/// One file that was offered to the parser and what came of it. <see cref="Plays"/> counts
/// what the file held, before plays shared with another file were left out.
/// </summary>
public sealed record LoadedFile(
    string Path, string Name, LoadedFileFormat Format, int Plays, DateTime? First, DateTime? Last, string? Problem);

/// <summary>Where the history on screen came from, for the panel that lists it.</summary>
public sealed record LoadedHistory(
    IReadOnlyList<LoadedFile> Files, int DuplicatesRemoved, string? Source, bool FromCache, bool IsSample,
    int Plays, DateTime? First, DateTime? Last);
