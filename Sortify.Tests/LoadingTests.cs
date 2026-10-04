using System.IO;
using Sortify.Services;
using Sortify.ViewModels;
using Xunit;

namespace Sortify.Tests;

/// <summary>Opening, dropping and reopening exports through the main view model.</summary>
[Collection(WpfCollection.Name)]
public class LoadingTests : IDisposable
{
    private const string TwoPlays = """
        [
            { "ts": "2023-05-01T10:00:00Z", "ms_played": 200000, "master_metadata_track_name": "One",
              "master_metadata_album_artist_name": "Artist", "master_metadata_album_album_name": "Album" },
            { "ts": "2023-05-02T10:00:00Z", "ms_played": 200000, "master_metadata_track_name": "Two",
              "master_metadata_album_artist_name": "Artist", "master_metadata_album_album_name": "Album" }
        ]
        """;

    private readonly string _dir;

    public LoadingTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "SortifyLoading_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        RecordCache.Clear();
    }

    public void Dispose()
    {
        RecordCache.Clear();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>An export folder holding one extended-history file with the given content.</summary>
    private string Export(string name, string json)
    {
        var folder = Path.Combine(_dir, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Streaming_History_Audio_2023.json"), json);
        return folder;
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task OpeningAFolder_LoadsAndRemembersIt()
    {
        var folder = Export("export", TwoPlays);
        var settings = new AppSettings();

        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(settings);
            await vm.OpenFolderPathAsync(folder);

            Assert.True(vm.HasData);
            Assert.False(vm.IsBusy);
            Assert.Equal("2", vm.TotalPlaysText);
            Assert.Equal(folder, settings.LastFolder);
            Assert.Equal(new[] { folder }, settings.RecentFolders);
        });
    }

    [Fact]
    public async Task AFileWithNoPlays_LeavesTheOpenHistoryAlone()
    {
        var folder = Export("export", TwoPlays);
        var stray = WriteFile("notes.json", "[]");

        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(new AppSettings());
            await vm.OpenFolderPathAsync(folder);
            await vm.LoadDroppedAsync(new[] { stray });

            Assert.True(vm.HasData);
            Assert.Equal("2", vm.TotalPlaysText);
            Assert.True(vm.StatusIsError);
            Assert.Contains("still open", vm.StatusText);
        });
    }

    [Fact]
    public async Task AFolderWithNoPlays_IsNotRemembered()
    {
        var empty = Export("empty", "[]");
        var settings = new AppSettings();

        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(settings);
            await vm.OpenFolderPathAsync(empty);

            Assert.False(vm.HasData);
            Assert.True(vm.StatusIsError);
            Assert.Null(settings.LastFolder);
            Assert.Empty(settings.RecentFolders);
        });
    }

    [Fact]
    public async Task DroppingAFolder_LoadsTheHistoryInsideIt()
    {
        var folder = Export("export", TwoPlays);
        var settings = new AppSettings();

        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(settings);
            await vm.LoadDroppedAsync(new[] { folder });

            Assert.True(vm.HasData);
            Assert.Equal("2", vm.TotalPlaysText);
            Assert.Equal(folder, settings.LastFolder);
        });
    }

    [Fact]
    public async Task DroppingSomethingElse_SaysWhatToDrop()
    {
        var text = WriteFile("readme.txt", "hello");

        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(new AppSettings());
            await vm.LoadDroppedAsync(new[] { text });

            Assert.False(vm.HasData);
            Assert.False(vm.IsBusy);
            Assert.True(vm.StatusIsError);
            Assert.Contains("Drop the ZIP", vm.StatusText);
        });
    }

    [Fact]
    public async Task RestoringAFolderWithNoHistory_SaysNothing()
    {
        var folder = Path.Combine(_dir, "nothing here");
        Directory.CreateDirectory(folder);
        var settings = new AppSettings { LastFolder = folder };

        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(settings);
            string before = vm.StatusText;
            await vm.RestoreLastFolderAsync();

            Assert.False(vm.HasData);
            Assert.False(vm.StatusIsError);
            Assert.Equal(before, vm.StatusText);
        });
    }
}
