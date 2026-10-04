using System.IO;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>The loaded history next to someone else's.</summary>
public class PeopleComparisonTests
{
    private static PlayRecord Play(string track, string artist, int minutes, int day = 1) => new()
    {
        TrackName = track,
        ArtistName = artist,
        AlbumName = artist + " LP",
        MsPlayed = minutes * 60_000,
        Timestamp = new DateTime(2024, 5, day, 12, 0, 0),
    };

    // Shared takes 75% of your time and 50% of theirs, so the overlap is 50%. Bit is under
    // the hour an artist needs to count as only theirs.
    private static readonly List<PlayRecord> Yours = new()
    {
        Play("Hit", "Shared", 360),
        Play("Mine", "Only Mine", 120),
    };

    private static readonly List<PlayRecord> Theirs = new()
    {
        Play("Hit", "Shared", 120),
        Play("Theirs", "Only Theirs", 70),
        Play("Blip", "Bit", 50),
    };

    private static Task<PeriodComparisonResult> Compare(FilterOptions? filter = null) =>
        PeriodComparison.ComparePeopleAsync(Yours, Theirs, filter ?? new FilterOptions());

    [Fact]
    public async Task TheOverlap_CountsEachArtistForTheSmallerShare()
    {
        var result = await Compare();

        Assert.Equal(50, result.OverlapPercent, precision: 6);
        Assert.Equal(1, result.ArtistsInCommon);
        var shared = Assert.Single(result.SharedArtists);
        Assert.Equal(("Shared", 360 * 60_000L, 120 * 60_000L), (shared.Name, shared.MsB, shared.MsA));
    }

    [Fact]
    public async Task IdenticalListening_Overlaps_Completely_AndNoneInCommon_NotAtAll()
    {
        var same = await PeriodComparison.ComparePeopleAsync(Yours, Yours, new FilterOptions());
        Assert.Equal(100, same.OverlapPercent, precision: 6);

        var apart = await PeriodComparison.ComparePeopleAsync(
            new[] { Play("A", "Mine Alone", 60) }, new[] { Play("B", "Theirs Alone", 60) }, new FilterOptions());
        Assert.Equal((0d, 0), (apart.OverlapPercent, apart.ArtistsInCommon));
        Assert.Empty(apart.SharedArtists);
    }

    [Fact]
    public async Task TheTables_RankByYourListening_WithTheirsAfter()
    {
        var result = await Compare();

        Assert.Equal(new[] { "Shared", "Only Mine", "Only Theirs", "Bit" }, result.Artists.Select(r => r.Name));
        Assert.Equal((1, 1), (result.Artists[0].RankB, result.Artists[0].RankA));
        Assert.Equal((null, 2), (result.Artists[2].RankB, result.Artists[2].RankA));
    }

    [Fact]
    public async Task OnlyYouAndOnlyThem_AreTheArtistsTheOtherNeverPlayed()
    {
        var result = await Compare();

        Assert.Equal("Only Mine", Assert.Single(result.NewInB).Name);
        Assert.Equal("Only Theirs", Assert.Single(result.GoneSinceA).Name);
    }

    [Fact]
    public async Task TheirRows_OpenNothing_SinceBreakdownsReadYourHistory()
    {
        var result = await Compare();

        Assert.Null(result.Artists.Single(r => r.Name == "Only Theirs").Item);
        Assert.Null(result.GoneSinceA[0].Item);
        Assert.NotNull(result.Artists.Single(r => r.Name == "Shared").Item);
        Assert.Null(DetailWindow.TargetFor(result.GoneSinceA[0]));
        Assert.Equal((DetailScope.Artist, "Only Mine", string.Empty), DetailWindow.TargetFor(result.NewInB[0]));
    }

    [Fact]
    public async Task TheFigures_SayDifferentRatherThanChanged()
    {
        var result = await Compare();

        var top = result.Metrics.Single(m => m.Metric == "Top artist");
        Assert.Equal(("Shared", "Shared", "same"), (top.B, top.A, top.Change));
        var time = result.Metrics.Single(m => m.Metric == "Listening time");
        Assert.Equal("+100%", time.Change);

        var nothing = await PeriodComparison.ComparePeopleAsync(Yours, Theirs, new FilterOptions { StartDate = new DateTime(2025, 1, 1) });
        Assert.Equal("same", nothing.Metrics.Single(m => m.Metric == "Plays").Change);

        var differentTop = PeriodComparison.Build(
            AnalysisEngine.Analyze(new[] { Play("X", "Them Top", 60) }, new FilterOptions()),
            AnalysisEngine.Analyze(Yours, new FilterOptions()), ComparisonKind.People);
        Assert.Equal("different", differentTop.Metrics.Single(m => m.Metric == "Top artist").Change);
        Assert.Equal("changed", PeriodComparison.Build(differentTop.A, differentTop.B).Metrics.Single(m => m.Metric == "Top artist").Change);
    }

    [Fact]
    public async Task EveryFilter_AppliesToBoth_DatesIncluded()
    {
        var filter = new FilterOptions { EndDate = new DateTime(2024, 5, 1, 23, 59, 59) };
        filter.ExcludedArtists.Add("Shared");

        var result = await PeriodComparison.ComparePeopleAsync(
            Yours.Append(Play("Later", "Only Mine", 600, day: 20)).ToList(), Theirs, filter);

        Assert.Equal(120 * 60_000L, result.B.TotalMsPlayed);
        Assert.Equal(120 * 60_000L, result.A.TotalMsPlayed);
        Assert.Equal(0, result.ArtistsInCommon);
    }
}

/// <summary>Opening someone else's export on the Compare tab.</summary>
public class PeopleCompareViewModelTests : IDisposable
{
    private const string TheirExport = """
        [
            { "ts": "2024-05-01T12:00:00Z", "ms_played": 3600000, "master_metadata_track_name": "Hit",
              "master_metadata_album_artist_name": "Shared", "master_metadata_album_album_name": "LP" },
            { "ts": "2024-05-03T12:00:00Z", "ms_played": 3600000, "master_metadata_track_name": "Theirs",
              "master_metadata_album_artist_name": "Only Theirs", "master_metadata_album_album_name": "LP" }
        ]
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SortifyPeople_" + Guid.NewGuid().ToString("N"));

    public PeopleCompareViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Export(string json = TheirExport)
    {
        var folder = Path.Combine(_dir, "their_export");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Streaming_History_Audio_2024.json"), json);
        return folder;
    }

    private static readonly List<PlayRecord> Yours = new()
    {
        new() { TrackName = "Hit", ArtistName = "Shared", AlbumName = "LP", MsPlayed = 10_800_000, Timestamp = new DateTime(2024, 5, 2, 12, 0, 0) },
        new() { TrackName = "Mine", ArtistName = "Only Mine", AlbumName = "LP", MsPlayed = 3_600_000, Timestamp = new DateTime(2024, 5, 4, 12, 0, 0) },
    };

    private static CompareViewModel Model(IReadOnlyList<string>? yourFiles = null) =>
        new(() => new CompareViewModel.Source(Yours, new FilterOptions(), TimeSpan.FromMinutes(30), yourFiles))
        {
            IsPeopleMode = true,
        };

    private static Task Load(CompareViewModel vm, string folder) =>
        vm.LoadOtherAsync(() => HistoryParser.FindHistoryFiles(folder), Path.GetFileName(folder));

    [Fact]
    public async Task TheirExport_LoadsIntoMemory_AndNeverIntoTheCache()
    {
        string folder = Export();
        var vm = Model();
        Assert.True(vm.CanOpenOther);

        await Load(vm, folder);

        Assert.True(vm.HasOther);
        Assert.False(vm.CanOpenOther);
        Assert.Equal("Their history: 2 plays from 2024-05-01 to 2024-05-03, read from their_export.", vm.OtherSummary);
        Assert.Null(RecordCache.TryLoad(HistoryParser.FindHistoryFiles(folder)));
    }

    [Fact]
    public async Task OnceLoaded_TheTabShowsTheOverlap()
    {
        var vm = Model();
        await Load(vm, Export());

        vm.IsVisible = true;
        await vm.RefreshAsync();

        Assert.Equal("1 artist in common", vm.InCommonText);
        Assert.Equal("50%", vm.OverlapText);
        Assert.Equal("Shared", Assert.Single(vm.SharedArtists).Name);
        Assert.Equal("Only Theirs", Assert.Single(vm.GoneSinceA).Name);
        Assert.Equal(string.Empty, vm.Message);
    }

    [Fact]
    public async Task AFolderWithoutHistory_SaysSo()
    {
        var empty = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(empty);
        var vm = Model();

        await Load(vm, empty);

        Assert.False(vm.HasOther);
        Assert.StartsWith("No Spotify history was found there.", vm.Message);
    }

    [Fact]
    public async Task YourOwnExport_IsTurnedAway()
    {
        string folder = Export();
        var vm = Model(yourFiles: HistoryParser.FindHistoryFiles(folder));

        await Load(vm, folder);

        Assert.False(vm.HasOther);
        Assert.Equal("That's the export already open as yours. Pick the other person's.", vm.Message);
    }

    [Fact]
    public async Task FilesWithNoPlays_LeaveNothingOpen()
    {
        var vm = Model();

        await Load(vm, Export("[]"));

        Assert.False(vm.HasOther);
        Assert.StartsWith("No listening data was found in their files.", vm.Message);
    }

    [Fact]
    public async Task ClosingTheirHistory_ClearsTheComparison()
    {
        var vm = Model();
        await Load(vm, Export());
        vm.IsVisible = true;
        await vm.RefreshAsync();

        vm.CloseOtherCommand.Execute(null);

        Assert.False(vm.HasOther);
        Assert.True(vm.CanOpenOther);
        Assert.Empty(vm.Artists);
        Assert.Equal(("-", string.Empty), (vm.OverlapText, vm.InCommonText));
    }

    [Fact]
    public async Task SwitchingModes_StartsTheResultsAfresh()
    {
        var vm = Model();
        await Load(vm, Export());
        vm.IsVisible = true;
        await vm.RefreshAsync();
        Assert.NotEmpty(vm.SharedArtists);

        // Hidden, so the switch doesn't start a period comparison that would refill the lists.
        vm.IsVisible = false;
        vm.IsPeriodsMode = true;

        Assert.False(vm.IsPeopleMode);
        Assert.Empty(vm.SharedArtists);
        Assert.True(vm.HasOther);
    }

    [Fact]
    public async Task WithoutTheirHistory_ThePeopleViewIsEmpty()
    {
        var vm = Model();
        vm.IsVisible = true;

        await vm.RefreshAsync();

        Assert.Empty(vm.Metrics);
        Assert.Equal(string.Empty, vm.Message);
    }
}
