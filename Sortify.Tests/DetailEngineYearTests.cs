using Sortify.Models;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

/// <summary>Covers the year drill-down and the podcast gate shared with the main analysis.</summary>
public class DetailEngineYearTests
{
    private static readonly FilterOptions NoFilter = new() { MinMsPlayed = 0 };

    private static PlayRecord Music(string track, string artist, DateTime ts, int ms = 60_000)
        => new()
        {
            TrackName = track,
            ArtistName = artist,
            AlbumName = artist + " Album",
            MsPlayed = ms,
            Timestamp = ts,
            Kind = ContentKind.Music,
        };

    private static PlayRecord Podcast(string episode, string show, DateTime ts, int ms = 60_000)
        => new()
        {
            MsPlayed = ms,
            Timestamp = ts,
            Kind = ContentKind.Podcast,
            ShowName = show,
            EpisodeName = episode,
        };

    [Fact]
    public void YearScope_KeepsOnlyThatYear()
    {
        var records = new[]
        {
            Music("A", "X", new DateTime(2022, 6, 1, 12, 0, 0)),
            Music("B", "Y", new DateTime(2023, 3, 1, 12, 0, 0)),
            Music("C", "Y", new DateTime(2023, 9, 1, 12, 0, 0)),
        };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Year, "2023", string.Empty);

        Assert.Equal(2, d.PlayCount);
        Assert.Equal(120_000, d.TotalMsPlayed);
        Assert.Equal(new DateTime(2023, 3, 1, 12, 0, 0), d.FirstPlayed);
        Assert.Equal(new DateTime(2023, 9, 1, 12, 0, 0), d.LastPlayed);
    }

    [Fact]
    public void YearScope_RanksArtists()
    {
        var records = new[]
        {
            Music("A", "Loud", new DateTime(2023, 1, 1, 12, 0, 0), ms: 30_000),
            Music("B", "Loud", new DateTime(2023, 1, 2, 12, 0, 0), ms: 30_000),
            Music("C", "Quiet", new DateTime(2023, 1, 3, 12, 0, 0), ms: 10_000),
        };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Year, "2023", string.Empty);

        Assert.Equal(2, d.Artists.Count);
        Assert.Equal("Loud", d.Artists[0].Artist);
        Assert.Equal(60_000, d.Artists[0].TotalMsPlayed);
        Assert.Equal("A", d.Artists[0].FirstTrack);
    }

    [Fact]
    public void OtherScopes_DoNotBuildAnArtistList()
    {
        var records = new[] { Music("A", "X", new DateTime(2023, 1, 1, 12, 0, 0)) };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Artist, "X", string.Empty);

        Assert.Empty(d.Artists);
    }

    [Fact]
    public void YearScope_IgnoresUndatedPlays()
    {
        var records = new[]
        {
            Music("A", "X", DateTime.MinValue),
            Music("B", "X", new DateTime(2023, 1, 1, 12, 0, 0)),
        };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Year, "2023", string.Empty);

        Assert.Equal(1, d.PlayCount);
    }

    [Fact]
    public void YearScope_HasNoSpotifyLink()
    {
        var records = new[] { Music("A", "X", new DateTime(2023, 1, 1, 12, 0, 0)) };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Year, "2023", string.Empty);

        Assert.Equal(string.Empty, d.WebUrl);
    }

    [Fact]
    public void PodcastsAreExcludedUnlessTheFilterAsksForThem()
    {
        var records = new[]
        {
            Music("A", "X", new DateTime(2023, 1, 1, 12, 0, 0)),
            Podcast("Episode 1", "Show", new DateTime(2023, 1, 2, 12, 0, 0)),
        };

        var without = DetailEngine.Build(records, NoFilter, DetailScope.Year, "2023", string.Empty);
        var with = DetailEngine.Build(
            records, new FilterOptions { MinMsPlayed = 0, IncludePodcasts = true },
            DetailScope.Year, "2023", string.Empty);

        Assert.Equal(1, without.PlayCount);
        Assert.Equal(2, with.PlayCount);
    }

    [Fact]
    public void UnparseableYear_MatchesNothing()
    {
        var records = new[] { Music("A", "X", new DateTime(2023, 1, 1, 12, 0, 0)) };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Year, "not a year", string.Empty);

        Assert.Equal(0, d.PlayCount);
    }
}
