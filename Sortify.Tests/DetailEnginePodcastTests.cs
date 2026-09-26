using Sortify.Models;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

/// <summary>
/// When podcasts are folded into the music statistics, AnalysisEngine ranks them under their
/// show and episode names. Drilling into one of those rows has to match on the same names,
/// or the detail window opens on nothing.
/// </summary>
public class DetailEnginePodcastTests
{
    private static readonly FilterOptions WithPodcasts = new() { MinMsPlayed = 0, IncludePodcasts = true };
    private static readonly FilterOptions WithoutPodcasts = new() { MinMsPlayed = 0 };

    private static PlayRecord Episode(string show, string episode, int ms = 600_000, DateTime? ts = null) => new()
    {
        TrackName = "Unknown Track",
        ArtistName = "Unknown Artist",
        AlbumName = "Unknown Album",
        Kind = ContentKind.Podcast,
        ShowName = show,
        EpisodeName = episode,
        MsPlayed = ms,
        Timestamp = ts ?? new DateTime(2024, 3, 4, 9, 0, 0),
    };

    private static PlayRecord Music(string track, string artist, int ms = 180_000, DateTime? ts = null) => new()
    {
        TrackName = track,
        ArtistName = artist,
        AlbumName = "Album",
        MsPlayed = ms,
        Timestamp = ts ?? new DateTime(2024, 3, 4, 9, 0, 0),
    };

    /// <summary>The name a show is listed under in the Artists grid is the one the row carries.</summary>
    private static string ArtistRowName(IReadOnlyList<PlayRecord> records)
        => AnalysisEngine.Analyze(records, WithPodcasts).Artists[0].Artist;

    [Fact]
    public void ShowOpenedAsAnArtist_FindsItsPlays()
    {
        var records = new[]
        {
            Episode("Radiolab", "Bit Flip"),
            Episode("Radiolab", "Stochasticity"),
            Music("Song", "Band"),
        };

        var detail = DetailEngine.Build(records, WithPodcasts, DetailScope.Artist, ArtistRowName(records), string.Empty);

        Assert.Equal("Radiolab", detail.Title);
        Assert.Equal(2, detail.PlayCount);
        Assert.Equal(1_200_000, detail.TotalMsPlayed);
    }

    [Fact]
    public void ShowOpenedAsAnArtist_ListsEpisodesAsTracks()
    {
        var records = new[]
        {
            Episode("Radiolab", "Bit Flip", ms: 900_000),
            Episode("Radiolab", "Stochasticity", ms: 300_000),
        };

        var detail = DetailEngine.Build(records, WithPodcasts, DetailScope.Artist, "Radiolab", string.Empty);

        Assert.Equal(new[] { "Bit Flip", "Stochasticity" }, detail.Tracks.Select(t => t.Track));
        Assert.All(detail.Tracks, t => Assert.Equal("Radiolab", t.Artist));
    }

    [Fact]
    public void EpisodeOpenedAsATrack_MatchesOnEpisodeAndShow()
    {
        var records = new[]
        {
            Episode("Radiolab", "Bit Flip"),
            Episode("Reply All", "Bit Flip"),
        };

        var detail = DetailEngine.Build(records, WithPodcasts, DetailScope.Track, "Bit Flip", "Radiolab");

        Assert.Equal(1, detail.PlayCount);
    }

    [Fact]
    public void PodcastsStayOut_WhenTheFilterDoesNotCountThem()
    {
        var records = new[] { Episode("Radiolab", "Bit Flip") };

        var detail = DetailEngine.Build(records, WithoutPodcasts, DetailScope.Artist, "Radiolab", string.Empty);

        Assert.Equal(0, detail.PlayCount);
    }

    [Fact]
    public void MusicIsUnaffected_WhenPodcastsAreCounted()
    {
        var records = new[]
        {
            Music("Song", "Band"),
            Episode("Radiolab", "Bit Flip"),
        };

        var detail = DetailEngine.Build(records, WithPodcasts, DetailScope.Artist, "Band", string.Empty);

        Assert.Equal(1, detail.PlayCount);
        Assert.Equal("Song", Assert.Single(detail.Tracks).Track);
    }

    [Fact]
    public void YearDetail_RanksShowsAlongsideArtists()
    {
        var records = new[]
        {
            Music("Song", "Band", ms: 100_000, ts: new DateTime(2024, 1, 2, 8, 0, 0)),
            Episode("Radiolab", "Bit Flip", ms: 900_000, ts: new DateTime(2024, 6, 2, 8, 0, 0)),
        };

        var detail = DetailEngine.Build(records, WithPodcasts, DetailScope.Year, "2024", string.Empty);

        Assert.Equal(new[] { "Radiolab", "Band" }, detail.Artists.Select(a => a.Artist));
    }

    [Fact]
    public void YearDetail_TotalsAgreeWithTheYearsTab()
    {
        var records = new[]
        {
            Music("Song", "Band", ms: 100_000, ts: new DateTime(2024, 1, 2, 8, 0, 0)),
            Episode("Radiolab", "Bit Flip", ms: 900_000, ts: new DateTime(2024, 6, 2, 8, 0, 0)),
            Music("Other", "Band", ms: 50_000, ts: new DateTime(2023, 6, 2, 8, 0, 0)),
        };

        var year = AnalysisEngine.Analyze(records, WithPodcasts).Years.Single(y => y.Year == 2024);
        var detail = DetailEngine.Build(records, WithPodcasts, DetailScope.Year, "2024", string.Empty);

        Assert.Equal(year.TotalMsPlayed, detail.TotalMsPlayed);
        Assert.Equal(year.PlayCount, detail.PlayCount);
    }
}
