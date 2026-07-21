using System.Text.Json.Serialization;

namespace Sortify.Models;

/// <summary>What kind of content a play refers to. Spotify mixes all three into one file.</summary>
public enum ContentKind
{
    Music,
    Podcast,
    Audiobook,
}

/// <summary>
/// A single normalized listening event parsed from Spotify extended streaming history.
/// </summary>
public sealed class PlayRecord
{
    public string TrackName { get; init; } = "Unknown Track";
    public string ArtistName { get; init; } = "Unknown Artist";
    public string AlbumName { get; init; } = "Unknown Album";
    public int MsPlayed { get; init; }

    /// <summary>Local-time timestamp the play ended (Spotify "ts" field, converted from UTC).</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>Spotify "reason_end" (e.g. "trackdone", "fwdbtn", "endplay").</summary>
    public string? ReasonEnd { get; init; }

    /// <summary>
    /// True when the play was skipped. Spotify's own "skipped" flag is null across large
    /// stretches of real exports, so <see cref="HistoryParser"/> falls back to reason_end.
    /// </summary>
    public bool Skipped { get; init; }

    // ---- Content kind -------------------------------------------------------------------

    /// <summary>Music, podcast or audiobook. Music-only stats filter on this.</summary>
    public ContentKind Kind { get; init; } = ContentKind.Music;

    /// <summary>Podcast show name, or audiobook title. Empty for music.</summary>
    public string ShowName { get; init; } = string.Empty;

    /// <summary>Podcast episode name, or audiobook chapter title. Empty for music.</summary>
    public string EpisodeName { get; init; } = string.Empty;

    // ---- Playback context ---------------------------------------------------------------

    /// <summary>Device/app the play happened on ("windows", "android", "web_player", ...).</summary>
    public string Platform { get; init; } = string.Empty;

    /// <summary>Two-letter country the play was streamed from ("conn_country").</summary>
    public string Country { get; init; } = string.Empty;

    /// <summary>True when the play started in shuffle mode.</summary>
    public bool Shuffle { get; init; }

    /// <summary>True when the play happened offline.</summary>
    public bool Offline { get; init; }

    /// <summary>True when the play happened in a private session.</summary>
    public bool Incognito { get; init; }

    /// <summary>Spotify URI for the track/episode, used to build "open in Spotify" links.</summary>
    public string Uri { get; init; } = string.Empty;

    /// <summary>
    /// True when this row came from an export that carries playback context at all. The
    /// legacy account-data format has none, so shuffle/offline percentages must not count
    /// those rows in their denominator.
    /// </summary>
    public bool HasPlaybackFlags { get; init; }
}

/// <summary>
/// Raw shape of an entry in a Spotify streaming history JSON file. Covers both the
/// extended history export ("ts", "ms_played", "master_metadata_*") and the older
/// account-data export ("endTime", "msPlayed", "artistName", "trackName").
/// Only the fields Sortify needs are mapped.
/// </summary>
public sealed class SpotifyHistoryEntry
{
    // ---- Extended streaming history --------------------------------------------------------

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

    [JsonPropertyName("spotify_track_uri")]
    public string? TrackUri { get; set; }

    // ---- Podcasts and audiobooks -----------------------------------------------------------

    [JsonPropertyName("episode_name")]
    public string? EpisodeName { get; set; }

    [JsonPropertyName("episode_show_name")]
    public string? EpisodeShowName { get; set; }

    [JsonPropertyName("spotify_episode_uri")]
    public string? EpisodeUri { get; set; }

    [JsonPropertyName("audiobook_title")]
    public string? AudiobookTitle { get; set; }

    [JsonPropertyName("audiobook_chapter_title")]
    public string? AudiobookChapterTitle { get; set; }

    [JsonPropertyName("audiobook_uri")]
    public string? AudiobookUri { get; set; }

    // ---- Playback context --------------------------------------------------------------------

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("conn_country")]
    public string? ConnCountry { get; set; }

    [JsonPropertyName("shuffle")]
    public bool? Shuffle { get; set; }

    [JsonPropertyName("offline")]
    public bool? Offline { get; set; }

    [JsonPropertyName("incognito_mode")]
    public bool? IncognitoMode { get; set; }

    // ---- Legacy account-data history ("StreamingHistory0.json") ----------------------------

    [JsonPropertyName("endTime")]
    public string? LegacyEndTime { get; set; }

    [JsonPropertyName("msPlayed")]
    public long? LegacyMsPlayed { get; set; }

    [JsonPropertyName("artistName")]
    public string? LegacyArtistName { get; set; }

    [JsonPropertyName("trackName")]
    public string? LegacyTrackName { get; set; }
}
