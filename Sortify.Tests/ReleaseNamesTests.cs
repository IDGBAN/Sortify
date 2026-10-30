using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Xunit;

namespace Sortify.Tests;

/// <summary>Remasters and deluxe editions folded into the original release.</summary>
public class ReleaseNamesTests
{
    [Theory]
    [InlineData("Song - 2011 Remaster", "Song")]
    [InlineData("Song - Remastered 2011", "Song")]
    [InlineData("Song - Remastered", "Song")]
    [InlineData("Song - 2009 Remastered Version", "Song")]
    [InlineData("Song - 2002 Digital Remaster", "Song")]
    [InlineData("Song (Remastered)", "Song")]
    [InlineData("Song [Remastered 2015]", "Song")]
    [InlineData("Song - Mono", "Song")]
    [InlineData("Song - Mono Version", "Song")]
    [InlineData("Song - Stereo", "Song")]
    [InlineData("Song - Single Version", "Song")]
    [InlineData("Song - Album Version", "Song")]
    [InlineData("Song - Bonus Track", "Song")]
    [InlineData("Song - Remastered 2011 / Mono", "Song")]
    [InlineData("Song (feat. Someone) - 2011 Remaster", "Song (feat. Someone)")]
    [InlineData("Song - Live - 2011 Remaster", "Song - Live")]
    [InlineData("Album (Deluxe Edition)", "Album")]
    [InlineData("Album (Deluxe)", "Album")]
    [InlineData("Album (Super Deluxe Edition)", "Album")]
    [InlineData("Album [Deluxe Version]", "Album")]
    [InlineData("Album (The Deluxe Edition)", "Album")]
    [InlineData("Album (Expanded Edition)", "Album")]
    [InlineData("Album (20th Anniversary Edition)", "Album")]
    [InlineData("Album (10 Year Anniversary Edition)", "Album")]
    [InlineData("Album (Bonus Track Version)", "Album")]
    [InlineData("Album (Remastered & Expanded)", "Album")]
    [InlineData("Album (2015 Remaster)", "Album")]
    [InlineData("Album (Deluxe Edition) [Remastered]", "Album")]
    public void EditionTags_AreFoldedIntoTheOriginal(string name, string original)
    {
        Assert.Equal(original, ReleaseNames.Original(name));
    }

    [Theory]
    [InlineData("Song - Live")]
    [InlineData("Song - Live at Wembley / 2011 Remaster")]
    [InlineData("Song - Remix")]
    [InlineData("Song - Radio Edit")]
    [InlineData("Song - Acoustic")]
    [InlineData("Song - 2011")]
    [InlineData("Song (feat. Someone)")]
    [InlineData("Song (Taylor's Version)")]
    [InlineData("Album (Edition)")]
    [InlineData("Stereo Love")]
    [InlineData("Mono")]
    [InlineData("(Remastered)")]
    [InlineData("Song - ")]
    public void OtherVersions_AndOrdinaryTitles_StayAsTheyAre(string name)
    {
        Assert.Same(name, ReleaseNames.Original(name));
    }

    [Fact]
    public void OnlyRenamedPlaysAreCopied_AndShowsAreLeftAlone()
    {
        var original = new PlayRecord { TrackName = "Song", ArtistName = "Band", AlbumName = "Album" };
        var remaster = new PlayRecord { TrackName = "Song - 2011 Remaster", ArtistName = "Band", AlbumName = "Album (2011 Remaster)" };
        var episode = new PlayRecord { Kind = ContentKind.Podcast, ShowName = "Show (Deluxe)", EpisodeName = "Episode - Remastered", TrackName = "x - Remastered" };

        var combined = ReleaseNames.Combine(new[] { original, remaster, episode });

        Assert.Same(original, combined[0]);
        Assert.NotSame(remaster, combined[1]);
        Assert.Equal(("Song", "Album"), (combined[1].TrackName, combined[1].AlbumName));
        Assert.Same(episode, combined[2]);
    }

    [Fact]
    public void ARenamedPlay_KeepsEverythingElse()
    {
        var play = new PlayRecord
        {
            TrackName = "Song - 2011 Remaster",
            ArtistName = "Band",
            AlbumName = "Album (Deluxe Edition)",
            MsPlayed = 123_456,
            Timestamp = new DateTime(2024, 5, 1, 12, 0, 0),
            ReasonEnd = "fwdbtn",
            Skipped = true,
            Uri = "spotify:track:abc",
            Platform = "android",
            Country = "JP",
            Shuffle = true,
            Offline = true,
            Incognito = true,
            HasPlaybackFlags = true,
        };

        var copy = Assert.Single(ReleaseNames.Combine(new[] { play }));

        Assert.Equal(("Song", "Album"), (copy.TrackName, copy.AlbumName));
        var unchanged = typeof(PlayRecord).GetProperties()
            .Where(p => p.Name is not (nameof(PlayRecord.TrackName) or nameof(PlayRecord.AlbumName)
                or nameof(PlayRecord.DisplayTrack) or nameof(PlayRecord.DisplayAlbum)));
        foreach (var property in unchanged)
            Assert.Equal(property.GetValue(play), property.GetValue(copy));
    }

    [Fact]
    public void Analysis_CountsTheEditionsAsOneTrackAndAlbum()
    {
        var records = new[]
        {
            new PlayRecord { TrackName = "Song", ArtistName = "Band", AlbumName = "Album", MsPlayed = 200_000, Timestamp = new DateTime(2020, 1, 1) },
            new PlayRecord { TrackName = "Song - 2011 Remaster", ArtistName = "Band", AlbumName = "Album (Deluxe Edition)", MsPlayed = 200_000, Timestamp = new DateTime(2024, 1, 1) },
        };

        var apart = AnalysisEngine.Analyze(records, new FilterOptions());
        var together = AnalysisEngine.Analyze(ReleaseNames.Combine(records), new FilterOptions());

        Assert.Equal((2, 2), (apart.Tracks.Count, apart.Albums.Count));
        var track = Assert.Single(together.Tracks);
        Assert.Equal(("Song", 2), (track.Track, track.PlayCount));
        Assert.Equal("Album", Assert.Single(together.Albums).Album);
    }

    [Fact]
    public void TheSample_HasReReleasesToFold()
    {
        var sample = SampleHistory.Generate(lastDay: new DateTime(2026, 10, 3));

        Assert.Contains(sample, p => p.AlbumName.EndsWith("(Deluxe Edition)", StringComparison.Ordinal));
        Assert.Contains(sample, p => p.TrackName.EndsWith(" Remaster", StringComparison.Ordinal));
        int albums = sample.Select(p => p.AlbumName).Distinct().Count();
        Assert.True(ReleaseNames.Combine(sample).Select(p => p.AlbumName).Distinct().Count() < albums);
    }
}

/// <summary>The sidebar option, from the checkbox to every view.</summary>
public class CombineReleasesFilterTests
{
    [Fact]
    public void TheOption_IsAFilterLikeAnyOther()
    {
        var vm = new FilterViewModel();
        int changes = 0;
        vm.FiltersChanged += (_, _) => changes++;

        vm.CombineReleases = true;

        Assert.Equal(1, changes);
        Assert.Contains("Re-releases combined", vm.Describe());
        Assert.True(vm.Snapshot("Mine").CombineReleases);
        Assert.True(vm.RememberedSnapshot().CombineReleases);
        Assert.False(vm.Snapshot().SameFiltersAs(new FilterViewModel().Snapshot()));

        vm.ResetCommand.Execute(null);
        Assert.False(vm.CombineReleases);

        vm.Apply(new FilterPreset { CombineReleases = true });
        Assert.True(vm.CombineReleases);
    }
}

[Collection(WpfCollection.Name)]
public class CombineReleasesViewTests
{
    [Fact]
    public async Task TurningItOn_FoldsTheSamplesReReleases_Everywhere()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(new AppSettings());
            vm.Filters.ResetCommand.Execute(null);
            await vm.OpenSampleCommand.ExecuteAsync(null);
            int albumsApart = vm.Albums.Count;
            Assert.Contains(vm.Tracks, t => t.Track.EndsWith(" Remaster", StringComparison.Ordinal));
            Assert.Contains(vm.Suggestions, s => s.Name.EndsWith(" Remaster", StringComparison.Ordinal));

            vm.Filters.CombineReleases = true;
            await Wait.UntilAsync(() => vm.Albums.Count < albumsApart
                && !vm.Suggestions.Any(s => s.Name.EndsWith(" Remaster", StringComparison.Ordinal)));

            Assert.True(vm.Albums.Count < albumsApart);
            Assert.DoesNotContain(vm.Tracks, t => t.Track.EndsWith(" Remaster", StringComparison.Ordinal));
            // The sidebar's search and exclusion boxes offer only the names still on screen.
            Assert.DoesNotContain(vm.Suggestions, s => s.Name.EndsWith(" Remaster", StringComparison.Ordinal));

            // A breakdown opened from a combined row finds every edition's plays.
            var top = vm.Albums[0];
            var detail = await vm.BuildDetailAsync(DetailScope.Album, top.Album, top.Artist);
            Assert.Equal(top.PlayCount, detail!.PlayCount);
        });
    }
}
