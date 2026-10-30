using System.IO;
using System.Text;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>What each file offered to the parser held, or why it wasn't read.</summary>
public class LoadedFileSummaryTests : IDisposable
{
    private const string Extended = """
        [
            { "ts": "2023-05-01T12:00:00Z", "ms_played": 200000, "master_metadata_track_name": "One",
              "master_metadata_album_artist_name": "Band", "master_metadata_album_album_name": "LP" },
            { "ts": "2023-05-02T12:00:00Z", "ms_played": 200000, "master_metadata_track_name": "Two",
              "master_metadata_album_artist_name": "Band", "master_metadata_album_album_name": "LP" }
        ]
        """;

    // The first play is the extended file's first, as the account-data export records it.
    private const string Legacy = """
        [
            { "endTime": "2023-05-01 12:00", "artistName": "Band", "trackName": "One", "msPlayed": 200000 },
            { "endTime": "2023-06-01 12:00", "artistName": "Band", "trackName": "Three", "msPlayed": 200000 }
        ]
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SortifyLoaded_" + Guid.NewGuid().ToString("N"));

    public LoadedFileSummaryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task EachFile_SaysWhatItHeld_OrWhyItWasNotRead()
    {
        var paths = new[]
        {
            Write("Streaming_History_Audio_2023.json", Extended),
            Write("StreamingHistory0.json", Legacy),
            Write("Streaming_History_Video.json", string.Empty),
            Write("Userdata.json", "{ \"username\": \"someone\" }"),
            Write("Streaming_History_Other.json", "[{}]"),
            Write("Streaming_History_Broken.json", "[\n{ \"ms_played\": \"lots\" }\n]"),
        };

        var parsed = await new HistoryParser().ParseAsync(paths);

        Assert.Equal(paths, parsed.Files.Select(f => f.Path));
        Assert.Equal(
            new[]
            {
                LoadedFileFormat.Extended, LoadedFileFormat.AccountData,
                LoadedFileFormat.Skipped, LoadedFileFormat.Skipped, LoadedFileFormat.Skipped, LoadedFileFormat.Skipped,
            },
            parsed.Files.Select(f => f.Format));
        Assert.Equal(new[] { 2, 2, 0, 0, 0, 0 }, parsed.Files.Select(f => f.Plays));
        Assert.Equal(1, parsed.DuplicatesRemoved);

        var extended = parsed.Files[0];
        Assert.Equal(new DateTime(2023, 5, 1, 12, 0, 0, DateTimeKind.Utc).ToLocalTime(), extended.First);
        Assert.Equal(new DateTime(2023, 5, 2, 12, 0, 0, DateTimeKind.Utc).ToLocalTime(), extended.Last);
        Assert.Null(extended.Problem);

        Assert.Equal("Empty", parsed.Files[2].Problem);
        Assert.Equal("Not a listening history", parsed.Files[3].Problem);
        Assert.Equal("Holds no plays Sortify can read", parsed.Files[4].Problem);
        Assert.Equal("Broken or unexpected JSON near line 2", parsed.Files[5].Problem);
    }

    [Fact]
    public void AFileInAZip_IsNamedWithTheZip()
    {
        Assert.Equal("Streaming_History_Audio_2019.json (in my_spotify_data.zip)",
            HistoryParser.DisplayName(ArchivePath.Combine(@"C:\Downloads\my_spotify_data.zip",
                "Spotify Extended Streaming History/Streaming_History_Audio_2019.json")));
        Assert.Equal("endsong_0.json", HistoryParser.DisplayName(@"C:\export\endsong_0.json"));
    }
}

/// <summary>The cache keeps the file list, so a cached launch can show it too.</summary>
[Collection(DataFolderCollection.Name)]
public class LoadedFileCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SortifyLoadedCache_" + Guid.NewGuid().ToString("N"));

    public LoadedFileCacheTests()
    {
        Directory.CreateDirectory(_dir);
        RecordCache.Clear();
    }

    public void Dispose()
    {
        RecordCache.Clear();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void TheFileList_ComesBackWithThePlays()
    {
        var file = Path.Combine(_dir, "Streaming_History_Audio_2023.json");
        File.WriteAllText(file, "[]");
        var play = new PlayRecord { TrackName = "One", ArtistName = "Band", Timestamp = new DateTime(2023, 5, 1, 14, 0, 0) };
        var files = new[]
        {
            new LoadedFile(file, "Streaming_History_Audio_2023.json", LoadedFileFormat.Extended, 1,
                new DateTime(2023, 5, 1, 14, 0, 0), new DateTime(2023, 5, 1, 14, 0, 0), null),
            new LoadedFile(file + "x", "Streaming_History_Audio_2023.jsonx", LoadedFileFormat.Skipped, 0, null, null, "Empty"),
        };

        RecordCache.TrySave(new[] { file }, new[] { play }, files: files, duplicatesRemoved: 3);
        var cached = RecordCache.TryLoadHistory(new[] { file })!;

        Assert.Single(cached.Records);
        Assert.Equal(files, cached.Files);
        Assert.Equal(3, cached.DuplicatesRemoved);
    }

    [Fact]
    public void ACacheWrittenBeforeTheFileList_IsReadAgain()
    {
        var file = Path.Combine(_dir, "Streaming_History_Audio_2023.json");
        File.WriteAllText(file, "[]");
        Directory.CreateDirectory(AppPaths.DataDirectory);
        using (var writer = new BinaryWriter(File.Create(Path.Combine(AppPaths.DataDirectory, "records.cache")), Encoding.UTF8))
        {
            writer.Write("SRTFY\0"u8.ToArray());
            writer.Write(1);
            writer.Write(RecordCache.BuildKey(new[] { file }));
            writer.Write(0);
            writer.Write(0);
        }

        Assert.Null(RecordCache.TryLoad(new[] { file }));
    }
}

/// <summary>The status bar's link and the panel it opens.</summary>
[Collection(WpfCollection.Name)]
public class LoadedFilesViewTests : IDisposable
{
    private const string TwoPlays = """
        [
            { "ts": "2023-05-01T12:00:00Z", "ms_played": 200000, "master_metadata_track_name": "One",
              "master_metadata_album_artist_name": "Band", "master_metadata_album_album_name": "LP" },
            { "ts": "2023-05-02T12:00:00Z", "ms_played": 200000, "master_metadata_track_name": "Two",
              "master_metadata_album_artist_name": "Band", "master_metadata_album_album_name": "LP" }
        ]
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SortifyLoadedView_" + Guid.NewGuid().ToString("N"));

    public LoadedFilesViewTests()
    {
        Directory.CreateDirectory(_dir);
        RecordCache.Clear();
    }

    public void Dispose()
    {
        RecordCache.Clear();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task OpeningAnExport_DescribesItsFiles_FromTheCacheToo()
    {
        File.WriteAllText(Path.Combine(_dir, "Streaming_History_Audio_2023.json"), TwoPlays);
        File.WriteAllText(Path.Combine(_dir, "Streaming_History_Audio_2024.json"), "not json");

        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(new AppSettings());
            await vm.OpenFolderPathAsync(_dir);

            Assert.Equal("1 file loaded, 1 skipped", vm.LoadedFilesText);
            var fresh = vm.LoadedHistory!;
            Assert.Equal((2, _dir, false, false), (fresh.Plays, fresh.Source, fresh.FromCache, fresh.IsSample));
            Assert.Equal(2, fresh.Files.Count);

            var files = HistoryParser.FindHistoryFiles(_dir);
            await Wait.UntilAsync(() => RecordCache.TryLoadHistory(files) is not null);

            await vm.OpenFolderPathAsync(_dir);

            var cached = vm.LoadedHistory!;
            Assert.True(cached.FromCache);
            Assert.Equal(fresh.Files, cached.Files);
            Assert.Equal("1 file loaded, 1 skipped", vm.LoadedFilesText);
        });
    }

    [Fact]
    public async Task TheSample_SaysItIsMadeUp()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(new AppSettings());
            await vm.OpenSampleCommand.ExecuteAsync(null);

            Assert.Equal("About this sample", vm.LoadedFilesText);
            var history = vm.LoadedHistory!;
            Assert.True(history.IsSample);
            Assert.StartsWith("A made-up sample history: ", LoadedFilesWindow.Summary(history));
            Assert.Equal("Nothing was read from disk. Open your own export to see yours.", LoadedFilesWindow.SourceLine(history));
        });
    }

    private static LoadedFile Loaded(LoadedFileFormat format, string? problem = null) =>
        new(@"C:\export\" + format, format.ToString(), format, format == LoadedFileFormat.Skipped ? 0 : 10,
            format == LoadedFileFormat.Skipped ? null : new DateTime(2023, 1, 1), format == LoadedFileFormat.Skipped ? null : new DateTime(2023, 12, 31), problem);

    private static LoadedHistory History(params LoadedFile[] files) =>
        new(files, 0, @"C:\export", false, false, 10, new DateTime(2023, 1, 1), new DateTime(2023, 12, 31));

    [Fact]
    public void TheAccountDataNote_ShowsOnlyWithoutTheExtendedHistory()
    {
        Assert.True(LoadedFilesWindow.OnlyAccountData(History(Loaded(LoadedFileFormat.AccountData), Loaded(LoadedFileFormat.Skipped, "Empty"))));
        Assert.False(LoadedFilesWindow.OnlyAccountData(History(Loaded(LoadedFileFormat.AccountData), Loaded(LoadedFileFormat.Extended))));
        Assert.False(LoadedFilesWindow.OnlyAccountData(History(Loaded(LoadedFileFormat.Skipped, "Empty"))));
    }

    [Fact]
    public void EachRow_SaysWhatTheFileHeld()
    {
        var read = LoadedFilesWindow.Row(Loaded(LoadedFileFormat.Extended));
        Assert.Equal(("Extended history", "10 plays, 2023-01-01 to 2023-12-31", false), (read.Format, read.Details, read.IsProblem));

        var skipped = LoadedFilesWindow.Row(Loaded(LoadedFileFormat.Skipped, "Couldn't be read (locked)"));
        Assert.Equal(("Skipped", "Couldn't be read (locked)", true), (skipped.Format, skipped.Details, skipped.IsProblem));

        Assert.Equal("Read 10 plays from 1 file, covering 2023-01-01 to 2023-12-31.",
            LoadedFilesWindow.Summary(History(Loaded(LoadedFileFormat.Extended), Loaded(LoadedFileFormat.Skipped, "Empty"))));
    }

    [Fact]
    public void ThePanel_OpensForEveryKindOfLoad()
    {
        WpfTestHost.Run(() =>
        {
            var window = new LoadedFilesWindow(History(Loaded(LoadedFileFormat.AccountData)));
            Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.UIElement)window.FindName("AccountDataNote")).Visibility);

            var sample = new LoadedFilesWindow(new LoadedHistory(Array.Empty<LoadedFile>(), 0, null, false, true, 5, null, null));
            Assert.Equal(System.Windows.Visibility.Collapsed, ((System.Windows.UIElement)sample.FindName("FilesCard")).Visibility);
        });
    }
}
