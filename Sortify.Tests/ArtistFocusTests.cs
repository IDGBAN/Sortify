using System.Text.Json;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Xunit;

namespace Sortify.Tests;

/// <summary>The "only these artists" filter.</summary>
public class ArtistFocusTests
{
    private static PlayRecord Play(string artist, string track = "Song") => new()
    {
        TrackName = track,
        ArtistName = artist,
        AlbumName = "Album",
        MsPlayed = 200_000,
        Timestamp = new DateTime(2024, 5, 1, 12, 0, 0),
    };

    private static PlayRecord Episode(string show) => new()
    {
        Kind = ContentKind.Podcast,
        ShowName = show,
        EpisodeName = "Ep 1",
        MsPlayed = 600_000,
        Timestamp = new DateTime(2024, 5, 1, 8, 0, 0),
    };

    private static FilterViewModel WithKnownArtists(params string[] artists)
    {
        var vm = new FilterViewModel();
        vm.SetKnownArtists(artists);
        return vm;
    }

    [Fact]
    public void FilterEngine_KeepsOnlyThePickedArtists_IgnoringCase()
    {
        var filter = new FilterOptions();
        filter.IncludedArtists.Add("radiohead");
        var records = new[] { Play("Radiohead"), Play("Bjork"), Play("Radiohead", "Creep") };

        var kept = FilterEngine.Apply(records, filter).ToList();

        Assert.Equal(2, kept.Count);
        Assert.All(kept, r => Assert.Equal("Radiohead", r.ArtistName));
    }

    [Fact]
    public void FilterEngine_MatchesAPodcastByItsShow_AsTheExclusionsDo()
    {
        var filter = new FilterOptions();
        filter.IncludedArtists.Add("Morning Show");
        var records = new[] { Play("Radiohead"), Episode("Morning Show"), Episode("Other Show") };

        var kept = FilterEngine.Apply(records, filter).ToList();

        Assert.Equal("Morning Show", Assert.Single(kept).ShowName);
    }

    [Fact]
    public void NoPickedArtists_KeepsEveryone()
    {
        var records = new[] { Play("A"), Play("B"), Episode("Show") };

        Assert.Equal(3, FilterEngine.Apply(records, new FilterOptions()).Count());
    }

    [Fact]
    public void AChangedDateRange_KeepsThePickedArtists()
    {
        var filter = new FilterOptions();
        filter.IncludedArtists.Add("A");

        var copy = filter.WithDateRange(new DateTime(2024, 1, 1), null);

        Assert.Contains("A", copy.IncludedArtists);
    }

    [Fact]
    public void TheAnalysis_DescribesOnlyThePickedArtists()
    {
        var filter = new FilterOptions();
        filter.IncludedArtists.UnionWith(new[] { "A", "C" });
        var records = new[] { Play("A"), Play("B"), Play("B"), Play("C") };

        var r = AnalysisEngine.Analyze(records, filter);

        Assert.Equal(new[] { "A", "C" }, r.Artists.Select(a => a.Artist).Order());
        Assert.Equal(2, r.TotalPlays);
    }

    [Fact]
    public void TheChip_NamesASingleArtist_AndCountsSeveral()
    {
        var vm = new FilterViewModel();
        vm.IncludeArtist("Radiohead");
        Assert.Contains("Only Radiohead", vm.Describe());

        vm.IncludeArtist("Bjork");
        vm.IncludeArtist("Portishead");
        Assert.Contains("Only 3 artists", vm.Describe());

        vm.Chips().Single(c => c.Text.StartsWith("Only")).ClearCommand.Execute(null);
        Assert.Empty(vm.IncludedArtists);
    }

    [Fact]
    public void ThePickedArtists_ReachTheAnalysis()
    {
        var vm = new FilterViewModel();
        vm.IncludeArtist("Radiohead");

        Assert.Contains("Radiohead", vm.ToOptions().IncludedArtists);
    }

    [Fact]
    public void FocusingFromAGrid_ReplacesThePickAsOneChange()
    {
        var vm = new FilterViewModel();
        vm.IncludeArtist("Old");
        int changes = 0;
        vm.FiltersChanged += (_, _) => changes++;

        vm.FocusOnArtists(new[] { "A", "B", "a" });

        Assert.Equal(new[] { "A", "B" }, vm.IncludedArtists);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void SavedSets_KeepThePick_ButLaunchesStartWithoutIt()
    {
        var vm = new FilterViewModel();
        vm.IncludeArtist("Radiohead");

        var saved = vm.Snapshot("Radiohead only");
        Assert.Equal(new[] { "Radiohead" }, saved.IncludedArtists);
        Assert.Empty(vm.RememberedSnapshot().IncludedArtists);

        var other = new FilterViewModel();
        other.Apply(saved);
        Assert.Equal(new[] { "Radiohead" }, other.IncludedArtists);
        Assert.True(other.Snapshot().SameFiltersAs(saved));

        other.ResetCommand.Execute(null);
        Assert.Empty(other.IncludedArtists);
    }

    [Fact]
    public void ADifferentPick_IsADifferentSetOfFilters()
    {
        var a = new FilterPreset { IncludedArtists = { "A" } };
        var b = new FilterPreset { IncludedArtists = { "B" } };

        Assert.False(a.SameFiltersAs(b));
    }

    [Fact]
    public void ASettingsFileFromBefore_LoadsWithNoPick()
    {
        var preset = JsonSerializer.Deserialize<FilterPreset>("""{ "Name": "Old", "MinSeconds": 10 }""")!;
        preset.Normalize();

        Assert.Empty(preset.IncludedArtists);
    }

    [Fact]
    public void Normalize_TidiesTheList()
    {
        var preset = new FilterPreset { IncludedArtists = { " A ", "", "a", "B" } };

        preset.Normalize();

        Assert.Equal(new[] { "A", "B" }, preset.IncludedArtists);
    }

    [Fact]
    public void Typing_SuggestsTheBestMatchesFirst()
    {
        var vm = WithKnownArtists("Air Supply", "Fair Weather", "Air", "Hairdryer", "The Airport");

        vm.NewIncludedArtist = "air";

        Assert.Equal(new[] { "Air", "Air Supply", "The Airport", "Fair Weather", "Hairdryer" }, vm.IncludeSuggestions);
    }

    [Fact]
    public void Suggestions_LeaveOutArtistsAlreadyPicked_AndStopAtTheLimit()
    {
        var names = Enumerable.Range(1, 20).Select(i => $"Band {i}").ToArray();
        var vm = WithKnownArtists(names);
        vm.IncludeArtist("Band 1");

        vm.NewIncludedArtist = "band";

        Assert.Equal(FilterViewModel.IncludeSuggestionCount, vm.IncludeSuggestions.Count);
        Assert.DoesNotContain("Band 1", vm.IncludeSuggestions);
    }

    [Fact]
    public void Adding_UsesTheArtistsOwnSpelling()
    {
        var vm = WithKnownArtists("Radiohead", "Bjork");
        vm.NewIncludedArtist = "RADIOHEAD";

        vm.AddIncludedArtistCommand.Execute(null);

        Assert.Equal(new[] { "Radiohead" }, vm.IncludedArtists);
        Assert.Equal(string.Empty, vm.NewIncludedArtist);
    }

    [Fact]
    public void Adding_APartialName_TakesTheBestMatch()
    {
        var vm = WithKnownArtists("Bjork", "Radiohead");
        vm.NewIncludedArtist = "radio";

        vm.AddIncludedArtistCommand.Execute(null);

        Assert.Equal(new[] { "Radiohead" }, vm.IncludedArtists);
    }

    [Fact]
    public void Adding_ANameNobodyHas_IsRefusedWithAReason()
    {
        var vm = WithKnownArtists("Radiohead");
        vm.NewIncludedArtist = "Nobody";
        int changes = 0;
        vm.FiltersChanged += (_, _) => changes++;

        vm.AddIncludedArtistCommand.Execute(null);

        Assert.Empty(vm.IncludedArtists);
        Assert.Equal(0, changes);
        Assert.Contains("Nobody", vm.IncludeHint);

        vm.NewIncludedArtist = "Radio";
        Assert.Equal(string.Empty, vm.IncludeHint);
    }

    [Fact]
    public void BeforeAHistoryLoads_AnyNameIsTaken()
    {
        var vm = new FilterViewModel { NewIncludedArtist = "Anyone" };

        vm.AddIncludedArtistCommand.Execute(null);

        Assert.Equal(new[] { "Anyone" }, vm.IncludedArtists);
    }

    [Fact]
    public void PickingASuggestion_AddsThatOne()
    {
        var vm = WithKnownArtists("Air", "Air Supply");
        vm.NewIncludedArtist = "air";

        vm.AddIncludedArtistCommand.Execute("Air Supply");

        Assert.Equal(new[] { "Air Supply" }, vm.IncludedArtists);
    }
}

/// <summary>Focusing from the main window.</summary>
[Collection(WpfCollection.Name)]
public class ArtistFocusWindowTests
{
    [Fact]
    public async Task ShowingOnlyOneArtist_NarrowsEveryTab()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(new AppSettings());
            vm.Filters.ResetCommand.Execute(null);
            await vm.OpenSampleCommand.ExecuteAsync(null);
            string top = vm.Artists[0].Artist;

            vm.FocusOnArtistsFromGrid(new[] { top });
            await Task.Delay(800);

            Assert.Equal(top, Assert.Single(vm.Artists).Artist);
            Assert.Contains(vm.ActiveFilters, c => c.Text == $"Only {top}");

            vm.Filters.NewIncludedArtist = top[..3];
            Assert.DoesNotContain(top, vm.Filters.IncludeSuggestions);
        });
    }
}
