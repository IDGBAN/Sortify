using Sortify.Models;
using Sortify.Services;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>Breakdowns opened from the Podcasts tab, whatever the podcasts setting says.</summary>
public class PodcastDetailTests
{
    // The default: podcasts kept out of the music statistics.
    private static readonly FilterOptions Default = new() { MinMsPlayed = 0 };

    private static PlayRecord Episode(string show, string episode, ContentKind kind = ContentKind.Podcast,
        int ms = 600_000, string uri = "") => new()
        {
            Kind = kind,
            ShowName = show,
            EpisodeName = episode,
            MsPlayed = ms,
            Uri = uri,
            Timestamp = new DateTime(2024, 3, 4, 9, 0, 0),
        };

    private static readonly PlayRecord[] Records =
    {
        Episode("Radiolab", "Bit Flip", uri: "spotify:episode:bitflip"),
        Episode("Radiolab", "Bit Flip"),
        Episode("Radiolab", "Stochasticity"),
        Episode("Other Show", "Bit Flip"),
        new() { TrackName = "Song", ArtistName = "Radiolab", MsPlayed = 180_000, Timestamp = new DateTime(2024, 3, 4, 9, 0, 0) },
    };

    [Fact]
    public void AShow_CollectsItsEpisodesEvenWhenPodcastsAreNotCounted()
    {
        var detail = DetailEngine.Build(Records, Default, DetailScope.Show, "Radiolab", string.Empty);

        Assert.Equal(3, detail.PlayCount);
        Assert.Equal(new[] { "Bit Flip", "Stochasticity" }, detail.Tracks.Select(t => t.Track));
        Assert.Equal(ContentKind.Podcast, detail.Kind);
    }

    [Fact]
    public void AnEpisode_MatchesOnItsShowToo()
    {
        var detail = DetailEngine.Build(Records, Default, DetailScope.Episode, "Bit Flip", "Radiolab");

        Assert.Equal(2, detail.PlayCount);
        Assert.Equal("https://open.spotify.com/episode/bitflip", detail.WebUrl);
    }

    [Fact]
    public void AShowOpensThroughSearch()
    {
        var detail = DetailEngine.Build(Records, Default, DetailScope.Show, "Radiolab", string.Empty);

        Assert.Equal("https://open.spotify.com/search/Radiolab", detail.WebUrl);
    }

    [Fact]
    public void AnAudiobookIsCalledOne()
    {
        var records = new[] { Episode("The Hobbit", "Chapter 1", ContentKind.Audiobook) };

        var book = DetailEngine.Build(records, Default, DetailScope.Show, "The Hobbit", string.Empty);
        var chapter = DetailEngine.Build(records, Default, DetailScope.Episode, "Chapter 1", "The Hobbit");

        Assert.Equal("Audiobook", DetailWindow.ScopeLabel(book));
        Assert.Equal("Chapter", DetailWindow.ScopeLabel(chapter));
        Assert.Equal("Podcast", DetailWindow.ScopeLabel(new DetailResult { Scope = DetailScope.Show, Title = "x", Kind = ContentKind.Podcast }));
    }

    [Fact]
    public void ShowAndEpisodeRowsOpenTheirOwnBreakdowns()
    {
        Assert.Equal((DetailScope.Show, "Radiolab", ""), DetailWindow.TargetFor(new ShowStat { Show = "Radiolab" }));
        Assert.Equal((DetailScope.Episode, "Bit Flip", "Radiolab"),
            DetailWindow.TargetFor(new EpisodeStat { Episode = "Bit Flip", Show = "Radiolab" }));
    }

    [Fact]
    public void FiltersStillApply()
    {
        var filter = new FilterOptions { MinMsPlayed = 0, EndDate = new DateTime(2024, 1, 1) };

        var detail = DetailEngine.Build(Records, filter, DetailScope.Show, "Radiolab", string.Empty);

        Assert.Equal(0, detail.PlayCount);
    }
}
