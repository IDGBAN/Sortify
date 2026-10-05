using System.IO;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Xunit;

namespace Sortify.Tests;

/// <summary>The made-up history offered on the empty screen.</summary>
public class SampleHistoryTests
{
    private static readonly DateTime LastDay = new(2026, 3, 31);

    private static readonly Lazy<List<PlayRecord>> Sample = new(() => SampleHistory.Generate(lastDay: LastDay));

    [Fact]
    public void TheSameSeed_MakesTheSameHistory()
    {
        var again = SampleHistory.Generate(lastDay: LastDay);

        Assert.Equal(Sample.Value.Count, again.Count);
        for (int i = 0; i < again.Count; i += 997)
        {
            Assert.Equal(Sample.Value[i].Timestamp, again[i].Timestamp);
            Assert.Equal(Sample.Value[i].DisplayTrack, again[i].DisplayTrack);
            Assert.Equal(Sample.Value[i].DisplayArtist, again[i].DisplayArtist);
        }
    }

    [Fact]
    public void ADifferentSeed_MakesADifferentHistory()
    {
        var other = SampleHistory.Generate(seed: 99, lastDay: LastDay);

        Assert.NotEqual(Sample.Value[0].DisplayArtist, other[0].DisplayArtist);
    }

    [Fact]
    public void CoversTheDaysBeforeTheLastOne_InTimeOrder()
    {
        var plays = Sample.Value;
        var first = LastDay.AddDays(-(SampleHistory.Days - 1));

        Assert.All(plays, p => Assert.InRange(p.Timestamp, first, LastDay.AddDays(1.5)));
        Assert.True(plays[^1].Timestamp >= LastDay.AddDays(-14), "the history should run up to its last day");
        for (int i = 1; i < plays.Count; i++)
            Assert.True(plays[i].Timestamp > plays[i - 1].Timestamp, $"play {i} is out of order");
    }

    [Fact]
    public void HoldsMusicPodcastsAndAnAudiobook()
    {
        var plays = Sample.Value;

        Assert.True(plays.Count(p => p.Kind == ContentKind.Music) > 10_000);
        Assert.Contains(plays, p => p.Kind == ContentKind.Podcast);
        Assert.Contains(plays, p => p.Kind == ContentKind.Audiobook);
        Assert.All(plays.Where(p => p.Kind != ContentKind.Music), p =>
        {
            Assert.NotEmpty(p.ShowName);
            Assert.NotEmpty(p.EpisodeName);
        });
    }

    [Fact]
    public void RecordsPlaybackContext_ButNoSpotifyLinks()
    {
        var plays = Sample.Value;

        Assert.All(plays, p => Assert.True(p.HasPlaybackFlags));
        Assert.All(plays, p => Assert.Empty(p.Uri));
        Assert.Contains(plays, p => p.Shuffle);
        Assert.Contains(plays, p => p.Offline);
        Assert.Contains(plays, p => p.Incognito);
        Assert.True(plays.Select(p => Devices.FamilyOf(p.Platform)).Distinct().Count() >= 4);
        Assert.True(plays.Select(p => p.Country).Distinct().Count() >= 2);
    }

    [Fact]
    public void GivesEveryInsightSomethingToShow()
    {
        var r = AnalysisEngine.Analyze(Sample.Value, new FilterOptions());

        Assert.True(r.TotalSkips > 0);
        Assert.True(r.CompletedPlays > r.TotalSkips);
        Assert.True(r.LongestBreakDays >= 7, "the sample has a stretch with no listening");
        Assert.NotEmpty(r.ForgottenTracks);
        Assert.NotEmpty(r.ForgottenArtists);
        Assert.Contains(r.Milestones, m => m.Label.Contains("hours"));
        Assert.True(r.Years.Count >= 2);
        Assert.True(r.NewArtistsByMonth.Count > 12, "artists should keep being discovered");
        Assert.True(r.Shows.Count >= 3);

        // No single artist should take over the history, as one rarely does in a real one.
        Assert.InRange(r.TopArtistSharePercent, 3, 25);
    }
}

/// <summary>Opening the sample through the main view model.</summary>
[Collection(WpfCollection.Name)]
public class SampleHistoryLoadingTests
{
    [Fact]
    public async Task OpeningTheSample_ShowsItWithoutRememberingIt()
    {
        var settings = new AppSettings();

        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(settings);
            await vm.OpenSampleCommand.ExecuteAsync(null);

            Assert.True(vm.HasData);
            Assert.True(vm.IsSample);
            Assert.False(vm.IsBusy);
            Assert.NotEqual("-", vm.TotalPlaysText);
            Assert.Null(settings.LastFolder);
            Assert.Empty(settings.RecentFolders);

            // There are no files behind it to re-read.
            Assert.False(vm.ReloadCommand.CanExecute(null));
        });
    }

    [Fact]
    public async Task OpeningAnExport_ReplacesTheSample()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SortifySample_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Streaming_History_Audio_2023.json"), """
            [ { "ts": "2023-05-01T10:00:00Z", "ms_played": 200000, "master_metadata_track_name": "One",
                "master_metadata_album_artist_name": "Artist", "master_metadata_album_album_name": "Album" } ]
            """);
        RecordCache.Clear();

        try
        {
            await WpfTestHost.RunAsync(async () =>
            {
                var vm = new MainViewModel(new AppSettings());
                await vm.OpenSampleCommand.ExecuteAsync(null);
                await vm.OpenFolderPathAsync(dir);

                Assert.False(vm.IsSample);
                Assert.Equal("1", vm.TotalPlaysText);
                Assert.True(vm.ReloadCommand.CanExecute(null));
            });
        }
        finally
        {
            RecordCache.Clear();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
