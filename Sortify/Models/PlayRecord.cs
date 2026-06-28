using System.Text.Json.Serialization;

namespace Sortify.Models;

/// <summary>
/// A single normalized listening event parsed from Spotify extended streaming history.
/// </summary>
public sealed class PlayRecord
{
    public string TrackName { get; init; } = "Unknown Track";
    public string ArtistName { get; init; } = "Unknown Artist";
    public string AlbumName { get; init; } = "Unknown Album";
    public int MsPlayed { get; init; }

    /// <summary>UTC timestamp the play ended (Spotify "ts" field).</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>Spotify "reason_end" (e.g. "trackdone", "fwdbtn", "endplay").</summary>
    public string? ReasonEnd { get; init; }

    /// <summary>Spotify "skipped" flag, when present.</summary>
    public bool Skipped { get; init; }
}

/// <summary>
/// Raw shape of an entry in a Spotify extended streaming history JSON file.
/// Only the fields Sortify needs are mapped.
/// </summary>
public sealed class SpotifyHistoryEntry
{
    [JsonPropertyName("ts")]
    public string? Ts { get; set; }

    [JsonPropertyName("ms_played")]
    public long MsPlayed { get; set; }

    [JsonPropertyName("master_metadata_track_name")]
    public string? TrackName { get; set; }

    [JsonPropertyName("master_metadata_album_artist_name")]
    public string? ArtistName { get; set; }

    [JsonPropertyName("master_metadata_album_album_name")]
    public string? AlbumName { get; set; }

    [JsonPropertyName("reason_end")]
    public string? ReasonEnd { get; set; }

    [JsonPropertyName("skipped")]
    public bool? Skipped { get; set; }
}
