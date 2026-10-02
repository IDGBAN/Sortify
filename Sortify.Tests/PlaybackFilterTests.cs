using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Xunit;

namespace Sortify.Tests;

/// <summary>Filtering by shuffle, offline, private sessions, device and country.</summary>
public class PlaybackFilterTests
{
    private static PlayRecord Play(
        string track, bool shuffle = false, bool offline = false, bool incognito = false,
        string platform = "windows 10", string country = "CA", bool flagged = true) => new()
        {
            TrackName = track,
            ArtistName = "Artist",
            AlbumName = "Album",
            MsPlayed = 60_000,
            Timestamp = new DateTime(2023, 5, 10, 14, 0, 0),
            Shuffle = shuffle,
            Offline = offline,
            Incognito = incognito,
            Platform = platform,
            Country = country,
            HasPlaybackFlags = flagged,
        };

    // An account-data export row: no flags, no device, no country.
    private static PlayRecord Legacy(string track) => Play(track, platform: "", country: "", flagged: false);

    private static List<string> Kept(FilterOptions filter, params PlayRecord[] records) =>
        FilterEngine.Apply(records, filter).Select(r => r.TrackName).ToList();

    [Fact]
    public void ShuffleOnly_KeepsOnlyPlaysKnownToBeShuffled()
    {
        var kept = Kept(new FilterOptions { Shuffle = PlaybackMode.Only },
            Play("shuffled", shuffle: true), Play("in order"), Legacy("old"));

        Assert.Equal(new[] { "shuffled" }, kept);
    }

    [Fact]
    public void ShuffleLeftOut_KeepsPlaysThatNeverRecordedIt()
    {
        var kept = Kept(new FilterOptions { Shuffle = PlaybackMode.Exclude },
            Play("shuffled", shuffle: true), Play("in order"), Legacy("old"));

        Assert.Equal(new[] { "in order", "old" }, kept);
    }

    [Fact]
    public void OfflineAndPrivateFollowTheSameRules()
    {
        Assert.Equal(new[] { "offline" }, Kept(new FilterOptions { Offline = PlaybackMode.Only },
            Play("offline", offline: true), Play("online")));

        Assert.Equal(new[] { "public" }, Kept(new FilterOptions { Private = PlaybackMode.Exclude },
            Play("private", incognito: true), Play("public")));
    }

    [Fact]
    public void ADeviceLeftOut_DropsEveryPlatformInItsFamily()
    {
        var filter = new FilterOptions();
        filter.ExcludedDevices.Add("Mobile");

        var kept = Kept(filter,
            Play("phone", platform: "Android OS 13 API 33 (Google, Pixel 7)"),
            Play("tablet", platform: "iOS 17.1 (iPad13,4)"),
            Play("desktop", platform: "windows 10 (10.0.19045; x64)"),
            Legacy("old"));

        Assert.Equal(new[] { "desktop", "old" }, kept);
    }

    [Fact]
    public void ACountryLeftOut_DropsItsPlaysButNotUnknownOnes()
    {
        var filter = new FilterOptions();
        filter.ExcludedCountries.Add("us");

        var kept = Kept(filter, Play("home"), Play("trip", country: "US"), Legacy("old"));

        Assert.Equal(new[] { "home", "old" }, kept);
    }

    private static FilterViewModel WithChoices()
    {
        var vm = new FilterViewModel();
        vm.SetAvailablePlayback(
            new[] { ("Desktop", "Desktop", "Desktop"), ("Mobile", "Mobile", "Mobile") },
            new[] { ("CA", "CA", "Canada (CA)"), ("US", "US", "United States (US)") });
        return vm;
    }

    [Fact]
    public void UntickingADevice_LeavesItOut()
    {
        var vm = WithChoices();
        int changes = 0;
        vm.FiltersChanged += (_, _) => changes++;

        vm.Devices.Single(d => d.Key == "Mobile").IsSelected = false;

        Assert.Equal(1, changes);
        Assert.Equal(new[] { "Mobile" }, vm.ToOptions().ExcludedDevices);
        Assert.Contains("1 device left out", vm.Describe());
    }

    [Fact]
    public void TheDeviceChip_TicksEverythingBackOn()
    {
        var vm = WithChoices();
        vm.Devices[0].IsSelected = false;
        vm.Devices[1].IsSelected = false;

        vm.Chips().Single(c => c.Text == "2 devices left out").ClearCommand.Execute(null);

        Assert.All(vm.Devices, d => Assert.True(d.IsSelected));
        Assert.Empty(vm.ToOptions().ExcludedDevices);
    }

    [Fact]
    public void ModeChips_DescribeAndClearTheirMode()
    {
        var vm = new FilterViewModel { ShuffleMode = PlaybackMode.Only, PrivateMode = PlaybackMode.Exclude };

        Assert.Equal(new[] { "Shuffled plays only", "No private sessions" }, vm.Describe());

        vm.Chips().Single(c => c.Text == "No private sessions").ClearCommand.Execute(null);

        Assert.Equal(PlaybackMode.Any, vm.PrivateMode);
        Assert.Equal(PlaybackMode.Only, vm.ShuffleMode);
    }

    [Fact]
    public void ACountryLeftOutStaysLeftOut_WhenAnotherHistoryIsLoaded()
    {
        var vm = WithChoices();
        vm.Countries.Single(c => c.Key == "US").IsSelected = false;

        vm.SetAvailablePlayback(
            new[] { ("Desktop", "Desktop", "Desktop") },
            new[] { ("US", "US", "United States (US)"), ("GB", "GB", "United Kingdom (GB)") });

        Assert.False(vm.Countries.Single(c => c.Key == "US").IsSelected);
        Assert.True(vm.Countries.Single(c => c.Key == "GB").IsSelected);
    }

    [Fact]
    public void Presets_CarryThePlaybackFilters()
    {
        var vm = WithChoices();
        vm.ShuffleMode = PlaybackMode.Exclude;
        vm.Countries.Single(c => c.Key == "US").IsSelected = false;
        var preset = vm.Snapshot("Home only");

        var other = WithChoices();
        other.Apply(preset);

        Assert.Equal(PlaybackMode.Exclude, other.ShuffleMode);
        Assert.False(other.Countries.Single(c => c.Key == "US").IsSelected);
        Assert.True(other.Snapshot().SameFiltersAs(preset));
    }

    [Fact]
    public void Remembered_KeepsWhatToLeaveOutButNotShuffleOrOffline()
    {
        var vm = WithChoices();
        vm.ShuffleMode = PlaybackMode.Only;
        vm.OfflineMode = PlaybackMode.Exclude;
        vm.PrivateMode = PlaybackMode.Exclude;
        vm.Devices.Single(d => d.Key == "Mobile").IsSelected = false;

        var remembered = vm.RememberedSnapshot();

        Assert.Equal(PlaybackMode.Any, remembered.Shuffle);
        Assert.Equal(PlaybackMode.Any, remembered.Offline);
        Assert.Equal(PlaybackMode.Exclude, remembered.Private);
        Assert.Equal(new[] { "Mobile" }, remembered.ExcludedDevices);
    }

    [Fact]
    public void Reset_CountsEveryPlayAgain()
    {
        var vm = WithChoices();
        vm.OfflineMode = PlaybackMode.Only;
        vm.Countries[0].IsSelected = false;

        vm.ResetCommand.Execute(null);

        Assert.Empty(vm.Describe());
        Assert.All(vm.Countries, c => Assert.True(c.IsSelected));
    }

    [Theory]
    [InlineData("CA", "Canada (CA)")]
    [InlineData("zz", "ZZ")]
    public void CountryName_FallsBackToTheCode(string code, string expected)
    {
        Assert.Equal(expected, MainViewModel.CountryName(code));
    }
}
