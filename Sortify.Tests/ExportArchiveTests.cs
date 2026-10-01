using System.IO;
using System.IO.Compression;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

/// <summary>Opening the ZIP Spotify sends without extracting it first.</summary>
public class ExportArchiveTests : IDisposable
{
    private const string OnePlay = """
        [
            {
                "ts": "2023-05-01T10:00:00Z",
                "ms_played": 215000,
                "master_metadata_track_name": "Song",
                "master_metadata_album_artist_name": "Artist",
                "master_metadata_album_album_name": "Album"
            }
        ]
        """;

    private readonly string _dir;

    public ExportArchiveTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "SortifyZip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string WriteZip(string name, params (string Entry, string Content)[] entries)
    {
        var path = Path.Combine(_dir, name);
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(content);
        }
        return path;
    }

    [Fact]
    public void FindHistoryFiles_LooksInsideAZip()
    {
        var zip = WriteZip("my_spotify_data.zip",
            ("Spotify Extended Streaming History/Streaming_History_Audio_2023.json", OnePlay),
            ("Spotify Extended Streaming History/Streaming_History_Audio_2022.json", OnePlay),
            ("Spotify Extended Streaming History/ReadMeFirst_ExtendedStreamingHistory.pdf", "pdf"),
            ("Userdata.json", "{}"));

        var files = HistoryParser.FindHistoryFiles(zip);

        Assert.Equal(new[]
        {
            ArchivePath.Combine(zip, "Spotify Extended Streaming History/Streaming_History_Audio_2022.json"),
            ArchivePath.Combine(zip, "Spotify Extended Streaming History/Streaming_History_Audio_2023.json"),
        }, files);
    }

    [Fact]
    public void FindHistoryFiles_ZipFallsBackToTopLevelJson()
    {
        var zip = WriteZip("renamed.zip",
            ("my plays.json", OnePlay),
            ("nested/other.json", OnePlay));

        var files = HistoryParser.FindHistoryFiles(zip);

        Assert.Equal(new[] { ArchivePath.Combine(zip, "my plays.json") }, files);
    }

    [Fact]
    public void FindHistoryFiles_DamagedZipFindsNothing()
    {
        var zip = Path.Combine(_dir, "broken.zip");
        File.WriteAllText(zip, "this is not a zip file");

        Assert.Empty(HistoryParser.FindHistoryFiles(zip));
    }

    [Fact]
    public void FindHistoryFiles_MissingZipFindsNothing()
    {
        Assert.Empty(HistoryParser.FindHistoryFiles(Path.Combine(_dir, "gone.zip")));
    }

    [Fact]
    public async Task ParseAsync_ReadsRecordsStraightFromTheZip()
    {
        var zip = WriteZip("export.zip",
            ("Spotify Extended Streaming History/Streaming_History_Audio_2023.json", OnePlay));

        var result = await new HistoryParser().ParseAsync(HistoryParser.FindHistoryFiles(zip));

        var record = Assert.Single(result.Records);
        Assert.Equal("Song", record.TrackName);
        Assert.Equal(215000, record.MsPlayed);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task ParseAsync_ReadsManyEntriesOfOneZipAtOnce()
    {
        var entries = Enumerable.Range(0, 12)
            .Select(i => ($"Streaming_History_Audio_{2010 + i}.json", OnePlay.Replace("2023", $"{2010 + i}")))
            .ToArray();
        var zip = WriteZip("export.zip", entries);

        var result = await new HistoryParser().ParseAsync(HistoryParser.FindHistoryFiles(zip));

        Assert.Equal(12, result.Records.Count);
    }

    [Fact]
    public async Task ParseAsync_SkipsAnEntryThatIsNotThere()
    {
        var zip = WriteZip("export.zip", ("Streaming_History_Audio_2023.json", OnePlay));
        var missing = ArchivePath.Combine(zip, "Streaming_History_Audio_1999.json");

        var result = await new HistoryParser().ParseAsync(new[] { missing });

        Assert.Empty(result.Records);
        Assert.Equal(new[] { missing }, result.SkippedFiles);
    }

    [Fact]
    public async Task ParseAsync_WarnsWhenTheZipIsDamagedAfterItWasScanned()
    {
        var zip = WriteZip("export.zip", ("Streaming_History_Audio_2023.json", OnePlay));
        var files = HistoryParser.FindHistoryFiles(zip);

        // Cutting the file short loses the archive's directory, the way an interrupted
        // download would.
        using (var stream = new FileStream(zip, FileMode.Open))
            stream.SetLength(stream.Length / 2);

        var result = await new HistoryParser().ParseAsync(files);

        Assert.Empty(result.Records);
        Assert.Contains("ZIP", Assert.Single(result.Warnings));
    }

    [Fact]
    public void CacheKey_ChangesWhenTheZipChanges()
    {
        var zip = WriteZip("export.zip", ("Streaming_History_Audio_2023.json", OnePlay));
        var files = HistoryParser.FindHistoryFiles(zip);
        string before = RecordCache.BuildKey(files);

        Assert.Equal(before, RecordCache.BuildKey(files));

        File.SetLastWriteTimeUtc(zip, DateTime.UtcNow.AddMinutes(5));

        Assert.NotEqual(before, RecordCache.BuildKey(files));
    }

    [Fact]
    public void CacheKey_TellsEntriesOfOneZipApart()
    {
        var zip = WriteZip("export.zip",
            ("Streaming_History_Audio_2022.json", OnePlay),
            ("Streaming_History_Audio_2023.json", OnePlay));
        var files = HistoryParser.FindHistoryFiles(zip);

        Assert.NotEqual(RecordCache.BuildKey(files.Take(1)), RecordCache.BuildKey(files.Skip(1)));
    }

    [Fact]
    public void ArchivePath_RoundTrips()
    {
        var path = ArchivePath.Combine(@"C:\Downloads\my_spotify_data.zip", "folder/Streaming_History_Audio_2023.json");

        Assert.True(ArchivePath.TrySplit(path, out var archive, out var entry));
        Assert.Equal(@"C:\Downloads\my_spotify_data.zip", archive);
        Assert.Equal("folder/Streaming_History_Audio_2023.json", entry);
        Assert.Equal("Streaming_History_Audio_2023.json", Path.GetFileName(path));
    }

    [Theory]
    [InlineData(@"C:\Downloads\my_spotify_data.zip", true)]
    [InlineData(@"C:\Downloads\MY_SPOTIFY_DATA.ZIP", true)]
    [InlineData(@"C:\Downloads\Streaming_History_Audio_2023.json", false)]
    [InlineData(@"C:\Downloads\export.zip|Streaming_History_Audio_2023.json", false)]
    [InlineData(@"C:\Downloads", false)]
    public void ArchivePath_RecognisesOnlyAZipItself(string path, bool expected)
    {
        Assert.Equal(expected, ArchivePath.IsArchive(path));
    }

    [Fact]
    public void ArchivePath_DoesNotSplitAPlainPath()
    {
        Assert.False(ArchivePath.TrySplit(@"C:\Downloads\Streaming_History_Audio_2023.json", out _, out _));
    }
}
