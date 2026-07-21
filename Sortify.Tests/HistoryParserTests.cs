using System.Globalization;
using System.IO;
using Sortify.Models;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

public class HistoryParserTests : IDisposable
{
    private readonly string _dir;

    public HistoryParserTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "SortifyTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private async Task<string> WriteFileAsync(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    [Fact]
    public async Task Parses_ExtendedHistoryEntries()
    {
        var path = await WriteFileAsync("Streaming_History_Audio_2023.json", """
        [
            {
                "ts": "2023-05-01T10:00:00Z",
                "ms_played": 215000,
                "master_metadata_track_name": "Song",
                "master_metadata_album_artist_name": "Artist",
                "master_metadata_album_album_name": "Album",
                "reason_end": "trackdone",
                "skipped": false
            }
        ]
        """);

        var result = await new HistoryParser().ParseAsync(new[] { path });

        var record = Assert.Single(result.Records);
        Assert.Equal("Song", record.TrackName);
        Assert.Equal("Artist", record.ArtistName);
        Assert.Equal("Album", record.AlbumName);
        Assert.Equal(215000, record.MsPlayed);
        Assert.Equal("trackdone", record.ReasonEnd);
        Assert.False(record.Skipped);

        var expected = DateTime.Parse("2023-05-01T10:00:00Z", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal).ToLocalTime();
        Assert.Equal(expected, record.Timestamp);
    }

    [Fact]
    public async Task Parses_LegacyAccountDataEntries()
    {
        var path = await WriteFileAsync("StreamingHistory0.json", """
        [
            {
                "endTime": "2021-01-20 14:33",
                "artistName": "Legacy Artist",
                "trackName": "Legacy Song",
                "msPlayed": 123456
            }
        ]
        """);

        var result = await new HistoryParser().ParseAsync(new[] { path });

        var record = Assert.Single(result.Records);
        Assert.Equal("Legacy Song", record.TrackName);
        Assert.Equal("Legacy Artist", record.ArtistName);
        Assert.Equal(123456, record.MsPlayed);
        Assert.NotEqual(DateTime.MinValue, record.Timestamp);
    }

    [Fact]
    public async Task Skips_RowsWithNoUsableMetadata()
    {
        var path = await WriteFileAsync("Streaming_History_Audio.json", """
        [
            { "ts": "2023-05-01T10:00:00Z", "ms_played": 900000,
              "master_metadata_track_name": null, "master_metadata_album_artist_name": null },
            { "ts": "2023-05-01T11:00:00Z", "ms_played": 1000,
              "master_metadata_track_name": "Real Song", "master_metadata_album_artist_name": "Artist" }
        ]
        """);

        var result = await new HistoryParser().ParseAsync(new[] { path });

        var record = Assert.Single(result.Records);
        Assert.Equal("Real Song", record.TrackName);
    }

    [Fact]
    public async Task Parses_PodcastEpisodes_AsPodcastKind()
    {
        var path = await WriteFileAsync("Streaming_History_Audio.json", """
        [
            { "ts": "2023-05-01T10:00:00Z", "ms_played": 900000,
              "master_metadata_track_name": null, "master_metadata_album_artist_name": null,
              "episode_name": "Ep 12: Deep Dive", "episode_show_name": "Some Show",
              "spotify_episode_uri": "spotify:episode:abc123" }
        ]
        """);

        var result = await new HistoryParser().ParseAsync(new[] { path });

        var record = Assert.Single(result.Records);
        Assert.Equal(ContentKind.Podcast, record.Kind);
        Assert.Equal("Some Show", record.ShowName);
        Assert.Equal("Ep 12: Deep Dive", record.EpisodeName);
        Assert.Equal("spotify:episode:abc123", record.Uri);
    }

    [Fact]
    public async Task Parses_Audiobooks_AsAudiobookKind()
    {
        var path = await WriteFileAsync("Streaming_History_Audio.json", """
        [
            { "ts": "2023-05-01T10:00:00Z", "ms_played": 900000,
              "audiobook_title": "A Long Book", "audiobook_chapter_title": "Chapter 3" }
        ]
        """);

        var result = await new HistoryParser().ParseAsync(new[] { path });

        var record = Assert.Single(result.Records);
        Assert.Equal(ContentKind.Audiobook, record.Kind);
        Assert.Equal("A Long Book", record.ShowName);
        Assert.Equal("Chapter 3", record.EpisodeName);
    }

    [Fact]
    public async Task Skipped_FallsBackToReasonEnd_WhenFlagIsAbsent()
    {
        // Real exports leave "skipped" null across long stretches; reason_end still tells us.
        var path = await WriteFileAsync("Streaming_History_Audio.json", """
        [
            { "ms_played": 3000, "master_metadata_track_name": "A",
              "master_metadata_album_artist_name": "X", "reason_end": "fwdbtn" },
            { "ms_played": 200000, "master_metadata_track_name": "B",
              "master_metadata_album_artist_name": "X", "reason_end": "trackdone" }
        ]
        """);

        var result = await new HistoryParser().ParseAsync(new[] { path });

        Assert.True(result.Records[0].Skipped);
        Assert.False(result.Records[1].Skipped);
    }

    [Fact]
    public async Task Skipped_ExplicitFlagWins_OverReasonEnd()
    {
        var path = await WriteFileAsync("Streaming_History_Audio.json", """
        [
            { "ms_played": 3000, "master_metadata_track_name": "A",
              "master_metadata_album_artist_name": "X", "reason_end": "fwdbtn", "skipped": false }
        ]
        """);

        var result = await new HistoryParser().ParseAsync(new[] { path });

        Assert.False(Assert.Single(result.Records).Skipped);
    }

    [Fact]
    public async Task Parses_PlaybackContextFields()
    {
        var path = await WriteFileAsync("Streaming_History_Audio.json", """
        [
            { "ms_played": 200000, "master_metadata_track_name": "A",
              "master_metadata_album_artist_name": "X",
              "platform": "android", "conn_country": "CA",
              "shuffle": true, "offline": true, "incognito_mode": false,
              "spotify_track_uri": "spotify:track:xyz" }
        ]
        """);

        var result = await new HistoryParser().ParseAsync(new[] { path });

        var record = Assert.Single(result.Records);
        Assert.Equal("android", record.Platform);
        Assert.Equal("CA", record.Country);
        Assert.True(record.Shuffle);
        Assert.True(record.Offline);
        Assert.False(record.Incognito);
        Assert.Equal("spotify:track:xyz", record.Uri);
    }

    [Fact]
    public async Task PoolsRepeatedPlatformStrings_AcrossRecords()
    {
        var path = await WriteFileAsync("Streaming_History_Audio.json", """
        [
            { "ms_played": 1000, "master_metadata_track_name": "A",
              "master_metadata_album_artist_name": "X", "platform": "windows 10" },
            { "ms_played": 1000, "master_metadata_track_name": "B",
              "master_metadata_album_artist_name": "X", "platform": "windows 10" }
        ]
        """);

        var result = await new HistoryParser().ParseAsync(new[] { path });

        Assert.Same(result.Records[0].Platform, result.Records[1].Platform);
    }

    [Fact]
    public async Task ClampsNegativeAndMissingValues()
    {
        var path = await WriteFileAsync("Streaming_History_Audio.json", """
        [
            { "ms_played": -500, "master_metadata_track_name": "T", "master_metadata_album_artist_name": "A" }
        ]
        """);

        var result = await new HistoryParser().ParseAsync(new[] { path });

        var record = Assert.Single(result.Records);
        Assert.Equal(0, record.MsPlayed);
        Assert.Equal(DateTime.MinValue, record.Timestamp);
        Assert.Equal("Unknown Album", record.AlbumName);
    }

    [Fact]
    public async Task InvalidJson_ProducesWarningNotCrash()
    {
        var path = await WriteFileAsync("Streaming_History_Audio.json", "{ not valid json ]");

        var result = await new HistoryParser().ParseAsync(new[] { path });

        Assert.Empty(result.Records);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task EmptyAndMissingFiles_AreSkipped()
    {
        var empty = await WriteFileAsync("empty.json", "");
        var missing = Path.Combine(_dir, "does_not_exist.json");

        var result = await new HistoryParser().ParseAsync(new[] { empty, missing });

        Assert.Empty(result.Records);
        Assert.Equal(2, result.SkippedFiles.Count);
    }

    [Fact]
    public async Task MergesMultipleFiles_InSelectionOrder()
    {
        var first = await WriteFileAsync("a.json", """
        [ { "ms_played": 1000, "master_metadata_track_name": "First", "master_metadata_album_artist_name": "A" } ]
        """);
        var second = await WriteFileAsync("b.json", """
        [ { "ms_played": 1000, "master_metadata_track_name": "Second", "master_metadata_album_artist_name": "A" } ]
        """);

        var result = await new HistoryParser().ParseAsync(new[] { first, second });

        Assert.Equal(2, result.Records.Count);
        Assert.Equal("First", result.Records[0].TrackName);
        Assert.Equal("Second", result.Records[1].TrackName);
    }

    [Fact]
    public async Task FindHistoryFiles_PrefersSpotifyNamedFiles()
    {
        var sub = Path.Combine(_dir, "Spotify Extended Streaming History");
        Directory.CreateDirectory(sub);
        await File.WriteAllTextAsync(Path.Combine(sub, "Streaming_History_Audio_2023_1.json"), "[]");
        await File.WriteAllTextAsync(Path.Combine(_dir, "unrelated.json"), "[]");

        var files = HistoryParser.FindHistoryFiles(_dir);

        var file = Assert.Single(files);
        Assert.EndsWith("Streaming_History_Audio_2023_1.json", file);
    }

    [Fact]
    public async Task FindHistoryFiles_FallsBackToTopLevelJson()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "myexport.json"), "[]");

        var files = HistoryParser.FindHistoryFiles(_dir);

        var file = Assert.Single(files);
        Assert.EndsWith("myexport.json", file);
    }

    [Fact]
    public void FindHistoryFiles_MissingFolderReturnsEmpty()
    {
        Assert.Empty(HistoryParser.FindHistoryFiles(Path.Combine(_dir, "nope")));
    }
}
