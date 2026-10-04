using System.IO;
using Sortify.Models;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

/// <summary>Loading files that repeat each other's plays counts every play once.</summary>
public class OverlappingExportsTests : IDisposable
{
    private readonly string _dir;

    public OverlappingExportsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "SortifyOverlap_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Write(string name, string json)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    private static string Extended(string ts, string track = "Song", string artist = "Artist", int ms = 200_000) => $$"""
        { "ts": "{{ts}}", "ms_played": {{ms}}, "master_metadata_track_name": "{{track}}",
          "master_metadata_album_artist_name": "{{artist}}", "master_metadata_album_album_name": "Album",
          "spotify_track_uri": "spotify:track:abc", "platform": "windows" }
        """;

    private static string Legacy(string endTime, string track = "Song", string artist = "Artist", int ms = 200_000) => $$"""
        { "endTime": "{{endTime}}", "artistName": "{{artist}}", "trackName": "{{track}}", "msPlayed": {{ms}} }
        """;

    private static string Array(params string[] rows) => "[" + string.Join(",", rows) + "]";

    private static Task<ParseResult> Parse(params string[] paths) => new HistoryParser().ParseAsync(paths);

    [Fact]
    public async Task TwoCopiesOfOneExport_CountEachPlayOnce()
    {
        var rows = Array(Extended("2023-05-01T10:00:00Z"), Extended("2023-05-01T10:04:00Z"), Extended("2023-05-02T09:00:00Z"));
        var a = Write("Streaming_History_Audio_2023.json", rows);
        Directory.CreateDirectory(Path.Combine(_dir, "copy"));
        var b = Write(Path.Combine("copy", "Streaming_History_Audio_2023.json"), rows);

        var result = await Parse(a, b);

        Assert.Equal(3, result.Records.Count);
        Assert.Equal(3, result.DuplicatesRemoved);
    }

    [Fact]
    public async Task ARowRepeatedInsideOneFile_IsLeftAlone()
    {
        var a = Write("Streaming_History_Audio_2023.json",
            Array(Extended("2023-05-01T10:00:00Z"), Extended("2023-05-01T10:00:00Z")));
        var b = Write("Streaming_History_Audio_2024.json", Array(Extended("2024-01-01T10:00:00Z")));

        var result = await Parse(a, b);

        Assert.Equal(3, result.Records.Count);
        Assert.Equal(0, result.DuplicatesRemoved);
    }

    [Fact]
    public async Task ARepeatedRowKeepsTheMostCopiesOneFileHad()
    {
        var row = Extended("2023-05-01T10:00:00Z");
        var a = Write("a.json", Array(row, row));
        var b = Write("b.json", Array(row));

        var result = await Parse(a, b);

        Assert.Equal(2, result.Records.Count);
        Assert.Equal(1, result.DuplicatesRemoved);
    }

    [Fact]
    public async Task AccountDataPlaysTheExtendedHistoryHas_AreDropped()
    {
        var extended = Write("Streaming_History_Audio_2023.json", Array(Extended("2023-05-01T10:00:42Z")));
        var legacy = Write("StreamingHistory_music_0.json", Array(Legacy("2023-05-01 10:00")));

        var result = await Parse(extended, legacy);

        var record = Assert.Single(result.Records);
        Assert.Equal("spotify:track:abc", record.Uri);
        Assert.Equal(1, result.DuplicatesRemoved);
    }

    [Fact]
    public async Task AnAccountDataTimeRoundedUpToTheNextMinute_StillMatches()
    {
        var extended = Write("Streaming_History_Audio_2023.json", Array(Extended("2023-05-01T10:00:42Z")));
        var legacy = Write("StreamingHistory_music_0.json", Array(Legacy("2023-05-01 10:01")));

        var result = await Parse(extended, legacy);

        Assert.Single(result.Records);
    }

    [Fact]
    public async Task AccountDataNamesTheTrackArtist_WhereExtendedNamesTheAlbumArtist()
    {
        var extended = Write("Streaming_History_Audio_2023.json",
            Array(Extended("2023-05-01T10:00:42Z", artist: "Various Artists")));
        var legacy = Write("StreamingHistory_music_0.json",
            Array(Legacy("2023-05-01 10:00", artist: "The Real Artist")));

        var result = await Parse(extended, legacy);

        Assert.Equal("Various Artists", Assert.Single(result.Records).ArtistName);
    }

    [Fact]
    public async Task AccountDataPlaysNewerThanTheExtendedHistory_AreKept()
    {
        var extended = Write("Streaming_History_Audio_2023.json", Array(Extended("2023-05-01T10:00:42Z")));
        var legacy = Write("StreamingHistory_music_0.json",
            Array(Legacy("2023-05-01 10:00"), Legacy("2023-09-14 18:20", track: "Newer Song")));

        var result = await Parse(extended, legacy);

        Assert.Equal(2, result.Records.Count);
        Assert.Contains(result.Records, r => r.TrackName == "Newer Song");
        Assert.Equal(1, result.DuplicatesRemoved);
    }

    [Fact]
    public async Task EachExtendedPlayCoversOnlyOneAccountDataPlay()
    {
        var extended = Write("Streaming_History_Audio_2023.json", Array(Extended("2023-05-01T10:00:30Z", ms: 20_000)));
        var legacy = Write("StreamingHistory_music_0.json",
            Array(Legacy("2023-05-01 10:00", ms: 20_000), Legacy("2023-05-01 10:00", ms: 10_000)));

        var result = await Parse(extended, legacy);

        Assert.Equal(2, result.Records.Count);
    }

    [Fact]
    public async Task AccountDataPodcastsMatchTheirEpisode()
    {
        var extended = Write("Streaming_History_Audio_2023.json", """
            [ { "ts": "2023-05-01T10:30:10Z", "ms_played": 1800000,
                "episode_name": "Episode 12", "episode_show_name": "The Show" } ]
            """);
        var legacy = Write("StreamingHistory_podcast_0.json",
            Array(Legacy("2023-05-01 10:30", track: "Episode 12", artist: "The Show", ms: 1_800_000)));

        var result = await Parse(extended, legacy);

        Assert.Equal(ContentKind.Podcast, Assert.Single(result.Records).Kind);
    }

    [Fact]
    public async Task OnlyAccountData_IsLeftAlone()
    {
        var a = Write("StreamingHistory_music_0.json", Array(Legacy("2023-05-01 10:00"), Legacy("2023-05-01 10:04")));
        var b = Write("StreamingHistory_music_1.json", Array(Legacy("2023-06-01 10:00")));

        var result = await Parse(a, b);

        Assert.Equal(3, result.Records.Count);
        Assert.Equal(0, result.DuplicatesRemoved);
    }

    [Fact]
    public async Task UndatedRowsAreNeverTreatedAsDuplicates()
    {
        var row = """{ "ms_played": 1000, "master_metadata_track_name": "Song", "master_metadata_album_artist_name": "Artist" }""";
        var a = Write("a.json", Array(row));
        var b = Write("b.json", Array(row));

        var result = await Parse(a, b);

        Assert.Equal(2, result.Records.Count);
    }
}
