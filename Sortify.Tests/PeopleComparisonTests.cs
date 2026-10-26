using System.IO;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Xunit;

namespace Sortify.Tests;

/// <summary>Two people's histories, side by side.</summary>
public class PeopleComparisonTests
{
    private static PlayRecord Play(string track, string artist, int minutes, DateTime? when = null) => new()
    {
        TrackName = track,
        ArtistName = artist,
        AlbumName = "Album",
        MsPlayed = minutes * 60_000,
        Timestamp = when ?? new DateTime(2024, 6, 1, 12, 0, 0),
    };

    // You: 560 minutes. Them: 400 minutes.
    private static readonly List<PlayRecord> Yours = new()
    {
        Play("Anthem", "Shared Band", 300),
        Play("Ballad", "Shared Band", 100),
        Play("Solo", "Your Pick", 120),
        Play("Shy", "Barely Yours", 40),
    };

    private static readonly List<PlayRecord> Theirs = new()
    {
        Play("Anthem", "Shared Band", 100),
        Play("Other", "Shared Band", 100),
        Play("Theirs", "Their Pick", 200, new DateTime(2020, 1, 1)),
    };

    private static Task<PeopleComparisonResult> Compare(FilterOptions? filter = null) =>
        PeopleComparison.CompareAsync(Yours, Theirs, filter ?? new FilterOptions());

    [Fact]
    public async Task SharedArtists_CarryBothRanksAndTimes()
    {
        var result = await Compare();

        var shared = Assert.Single(result.SharedArtists);
        Assert.Equal("Shared Band", shared.Name);
        Assert.Equal((1, 1), (shared.RankA, shared.RankB));
        Assert.Equal((400, 200), (shared.HoursA * 60, shared.HoursB * 60));
        Assert.IsType<ArtistStat>(shared.Item);
        Assert.Equal(1, result.SharedArtistCount);
    }

    [Fact]
    public async Task SharedTracks_MatchOnTrackAndArtist()
    {
        var result = await Compare();

        var anthem = Assert.Single(result.SharedTracks);
        Assert.Equal(("Anthem", "Shared Band"), (anthem.Name, anthem.Secondary));
    }

    [Fact]
    public async Task Match_IsTheListeningTimeYouHaveInCommon()
    {
        var result = await Compare();

        // Shared Band: 400/560 of yours, 200/400 of theirs; the smaller is 50%.
        Assert.Equal(50, result.ArtistMatch, 3);
        // Anthem: 300/560 of yours, 100/400 of theirs.
        Assert.Equal(25, result.TrackMatch, 3);
    }

    [Fact]
    public void Match_IsWholeForTheSameHistoryAndZeroForNothingShared()
    {
        var same = PeopleComparison.Build(AnalysisEngine.Analyze(Yours, new FilterOptions()), AnalysisEngine.Analyze(Yours, new FilterOptions()));
        Assert.Equal(100, same.ArtistMatch, 3);

        var apart = PeopleComparison.Build(AnalysisEngine.Analyze(Yours, new FilterOptions()),
            AnalysisEngine.Analyze(new List<PlayRecord> { Play("X", "Stranger", 60) }, new FilterOptions()));
        Assert.Equal(0, apart.ArtistMatch);
        Assert.Empty(apart.SharedArtists);
    }

    [Fact]
    public async Task OnlyOneOfYou_NeedsAnHourOfListening()
    {
        var result = await Compare();

        Assert.Equal(new[] { "Your Pick" }, result.OnlyYou.Select(r => r.Name));
        Assert.Equal(new[] { "Their Pick" }, result.OnlyThem.Select(r => r.Name));
        Assert.Equal(2, result.OnlyThem[0].RankB);
    }

    [Fact]
    public async Task SidebarFiltersApplyToBothPeople()
    {
        var filter = new FilterOptions { StartDate = new DateTime(2023, 1, 1) };
        filter.ExcludedArtists.Add("Your Pick");

        var result = await Compare(filter);

        Assert.Empty(result.OnlyYou);
        Assert.Empty(result.OnlyThem);
        Assert.Equal(200, result.Them.TotalMsPlayed / 60_000);
    }

    [Fact]
    public async Task Metrics_PutYouInAAndThemInB()
    {
        var result = await Compare();
        var metrics = result.Metrics.ToDictionary(m => m.Metric);

        Assert.Equal("-29%", metrics["Listening time"].Change);
        Assert.Equal(("Shared Band", "Shared Band", "same"),
            (metrics["Top artist"].A, metrics["Top artist"].B, metrics["Top artist"].Change));
        Assert.Equal("different", metrics["Top track"].Change);
    }

    [Fact]
    public async Task BetweenTwoPeople_NothingIsNew()
    {
        var result = await Compare(new FilterOptions { StartDate = new DateTime(2025, 1, 1) });

        Assert.Equal("same", result.Metrics.Single(m => m.Metric == "Plays").Change);
    }
}

/// <summary>The Compare People tab's own behaviour.</summary>
public class PeopleCompareViewModelTests
{
    private static readonly List<PlayRecord> Plays = new()
    {
        new PlayRecord { TrackName = "Song", ArtistName = "Band", AlbumName = "Album", MsPlayed = 3_600_000, Timestamp = new DateTime(2024, 1, 2) },
    };

    private static CompareViewModel.Source? YourHistory() => new CompareViewModel.Source(Plays, new FilterOptions(), TimeSpan.FromMinutes(30));

    [Fact]
    public async Task WithNoOneLoaded_NothingIsCompared()
    {
        var vm = new PeopleCompareViewModel(YourHistory);

        await vm.RefreshAsync();

        Assert.False(vm.HasResults);
        Assert.Equal(string.Empty, vm.Message);
        Assert.False(vm.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task TheirHistory_IsComparedOnceTheTabIsShown()
    {
        int asked = 0;
        var vm = new PeopleCompareViewModel(() =>
        {
            asked++;
            return YourHistory();
        });

        vm.SetTheirs(Plays, "Sam");
        Assert.Equal(0, asked);

        vm.IsVisible = true;
        await Wait.UntilAsync(() => vm.HasResults);

        Assert.Equal(1, asked);
        Assert.True(vm.HasResults);
        Assert.Equal("100%", vm.ArtistMatch);
        Assert.Equal("Sam", vm.TheirName);
        Assert.Equal("Only Sam, Not You", vm.OnlyThemTitle);
        Assert.Contains("1 play loaded, 2024-01-02 to 2024-01-02", vm.LoadedText);
    }

    [Fact]
    public async Task WithReleasesCombined_TheirRemastersMeetYourOriginals()
    {
        var theirs = new List<PlayRecord>
        {
            new() { TrackName = "Song - 2011 Remaster", ArtistName = "Band", AlbumName = "Album (Deluxe Edition)", MsPlayed = 3_600_000, Timestamp = new DateTime(2024, 1, 3) },
        };
        bool combine = false;
        var vm = new PeopleCompareViewModel(() =>
            new CompareViewModel.Source(Plays, new FilterOptions(), TimeSpan.FromMinutes(30), CombineReleases: combine));
        vm.SetTheirs(theirs);

        await vm.RefreshAsync();
        Assert.Empty(vm.SharedTracks);

        combine = true;
        await vm.RefreshAsync();
        Assert.Equal("Song", Assert.Single(vm.SharedTracks).Name);
        Assert.Equal("Song - 2011 Remaster", theirs[0].TrackName);
    }

    [Fact]
    public async Task ASliverInCommon_ReadsAsUnderOnePercent()
    {
        var yours = new List<PlayRecord>
        {
            new() { TrackName = "Big", ArtistName = "Mostly Mine", AlbumName = "A", MsPlayed = 360_000_000, Timestamp = new DateTime(2024, 1, 2) },
            new() { TrackName = "Small", ArtistName = "Shared", AlbumName = "B", MsPlayed = 1_800_000, Timestamp = new DateTime(2024, 1, 2) },
        };
        var vm = new PeopleCompareViewModel(() => new CompareViewModel.Source(yours, new FilterOptions(), TimeSpan.FromMinutes(30)));
        vm.SetTheirs(new List<PlayRecord> { yours[1] });

        await vm.RefreshAsync();

        Assert.Equal("<1%", vm.ArtistMatch);
    }

    [Fact]
    public async Task ACancelledLoad_KeepsWhoWasThere()
    {
        var vm = new PeopleCompareViewModel(YourHistory);
        vm.SetTheirs(Plays, "Sam");
        using var looking = new ManualResetEventSlim();
        using var carryOn = new ManualResetEventSlim();

        // Finding nothing after the cancel would otherwise say so as if it were the answer.
        var load = vm.LoadTheirsAsync(() =>
        {
            looking.Set();
            carryOn.Wait();
            return Array.Empty<string>();
        }, "Alex");
        looking.Wait();
        Assert.True(vm.CancelLoadCommand.CanExecute(null));
        vm.CancelLoadCommand.Execute(null);
        carryOn.Set();
        await load;

        Assert.False(vm.IsLoading);
        Assert.True(vm.HasTheirs);
        Assert.Equal("Sam", vm.TheirName);
        Assert.Equal(string.Empty, vm.LoadError);
    }

    [Fact]
    public async Task WithoutYourOwnHistory_SaysToOpenItFirst()
    {
        var vm = new PeopleCompareViewModel(() => null);
        vm.SetTheirs(Plays);

        await vm.RefreshAsync();

        Assert.Contains("Open your own Spotify history first", vm.Message);
        Assert.False(vm.HasResults);
    }

    [Fact]
    public async Task Remove_ForgetsThem()
    {
        var vm = new PeopleCompareViewModel(YourHistory);
        vm.SetTheirs(Plays);
        await vm.RefreshAsync();

        vm.RemoveCommand.Execute(null);

        Assert.False(vm.HasTheirs);
        Assert.False(vm.HasResults);
        Assert.Empty(vm.SharedArtists);
    }

    [Fact]
    public async Task ALoadThatFindsNothing_KeepsWhoWasThereAndSaysSo()
    {
        var vm = new PeopleCompareViewModel(YourHistory);
        vm.SetTheirs(Plays, "Sam");

        await vm.LoadTheirsAsync(() => Array.Empty<string>(), "Someone Else");

        Assert.Contains("No Spotify history was found", vm.LoadError);
        Assert.True(vm.HasTheirs);
        Assert.Equal("Sam", vm.TheirName);
    }

    [Fact]
    public async Task Loading_ParsesTheirFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sortify-people-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "StreamingHistory0.json");
            File.WriteAllText(file,
                """[{"endTime":"2024-03-01 10:00","artistName":"Band","trackName":"Song","msPlayed":200000}]""");
            var vm = new PeopleCompareViewModel(YourHistory);

            await vm.LoadTheirsAsync(() => HistoryParser.FindHistoryFiles(dir), "Alex");

            Assert.True(vm.HasTheirs);
            Assert.Equal("Alex", vm.TheirName);
            Assert.Equal(string.Empty, vm.LoadError);
            Assert.StartsWith("1 play loaded", vm.LoadedText);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task YourOwnExport_IsTurnedAway()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sortify-people-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "StreamingHistory0.json"),
                """[{"endTime":"2024-03-01 10:00","artistName":"Band","trackName":"Song","msPlayed":200000}]""");
            var yourFiles = HistoryParser.FindHistoryFiles(dir);
            var vm = new PeopleCompareViewModel(() =>
                new CompareViewModel.Source(Plays, new FilterOptions(), TimeSpan.FromMinutes(30), yourFiles));

            await vm.LoadTheirsAsync(() => HistoryParser.FindHistoryFiles(dir), "Alex");

            Assert.False(vm.HasTheirs);
            Assert.Equal("That's the export already open as yours. Pick the other person's.", vm.LoadError);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(@"C:\exports\Alex", "Alex")]
    [InlineData(@"C:\exports\my_spotify_data.zip", null)]
    [InlineData(@"C:\exports\Spotify Extended Streaming History\", null)]
    public void NameFrom_OnlySuggestsAPersonsName(string path, string? expected) =>
        Assert.Equal(expected, PeopleCompareViewModel.NameFrom(path));
}
