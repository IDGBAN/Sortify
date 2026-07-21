using Sortify.Models;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

public class DetailEngineTests
{
    private static readonly FilterOptions NoFilter = new() { MinMsPlayed = 0 };

    private static PlayRecord Record(
        string track = "Track", string artist = "Artist", string album = "Album",
        int ms = 60_000, DateTime? ts = null, string uri = "")
        => new()
        {
            TrackName = track,
            ArtistName = artist,
            AlbumName = album,
            MsPlayed = ms,
            Timestamp = ts ?? new DateTime(2023, 5, 10, 14, 0, 0),
            Uri = uri,
        };

    [Fact]
    public void Artist_AggregatesOnlyThatArtistsPlays()
    {
        var records = new[]
        {
            Record(track: "A", artist: "X", album: "Al1", ms: 100_000),
            Record(track: "B", artist: "X", album: "Al2", ms: 50_000),
            Record(track: "C", artist: "Y", ms: 999_000),
        };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Artist, "X", string.Empty);

        Assert.Equal(150_000, d.TotalMsPlayed);
        Assert.Equal(2, d.PlayCount);
        Assert.Equal(2, d.Tracks.Count);
        Assert.Equal(2, d.Albums.Count);
        Assert.Equal("A", d.Tracks[0].Track);   // most listened first
        Assert.DoesNotContain(d.Tracks, t => t.Track == "C");
    }

    [Fact]
    public void Track_DistinguishesSameTitleByDifferentArtists()
    {
        var records = new[]
        {
            Record(track: "Intro", artist: "X", ms: 100_000),
            Record(track: "Intro", artist: "Y", ms: 50_000),
        };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Track, "Intro", "X");

        Assert.Equal(100_000, d.TotalMsPlayed);
        Assert.Equal(1, d.PlayCount);
    }

    [Fact]
    public void Album_MatchesOnAlbumAndArtist()
    {
        var records = new[]
        {
            Record(track: "A", artist: "X", album: "Greatest Hits", ms: 100_000),
            Record(track: "B", artist: "Y", album: "Greatest Hits", ms: 50_000),
        };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Album, "Greatest Hits", "X");

        Assert.Equal(100_000, d.TotalMsPlayed);
        Assert.Equal(1, d.PlayCount);
    }

    [Fact]
    public void RespectsActiveFilters()
    {
        var records = new[]
        {
            Record(artist: "X", ms: 100_000, ts: new DateTime(2023, 1, 1, 10, 0, 0)),
            Record(artist: "X", ms: 100_000, ts: new DateTime(2024, 1, 1, 10, 0, 0)),
        };
        var filter = new FilterOptions { MinMsPlayed = 0, EndDate = new DateTime(2023, 12, 31, 23, 59, 59) };

        var d = DetailEngine.Build(records, filter, DetailScope.Artist, "X", string.Empty);

        Assert.Equal(1, d.PlayCount);
    }

    [Fact]
    public void TracksFirstAndLastPlayed_SpanAllPlays()
    {
        var records = new[]
        {
            Record(track: "A", artist: "X", ts: new DateTime(2023, 5, 2, 10, 0, 0)),
            Record(track: "A", artist: "X", ts: new DateTime(2023, 5, 1, 10, 0, 0)),
            Record(track: "A", artist: "X", ts: new DateTime(2023, 5, 3, 10, 0, 0)),
        };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Artist, "X", string.Empty);

        Assert.Equal(new DateTime(2023, 5, 1, 10, 0, 0), d.FirstPlayed);
        Assert.Equal(new DateTime(2023, 5, 3, 10, 0, 0), d.LastPlayed);
        Assert.Equal(3, d.ActiveDays);
    }

    [Fact]
    public void ByMonth_BucketsPlaysIntoCalendarMonths()
    {
        var records = new[]
        {
            Record(artist: "X", ms: 3_600_000, ts: new DateTime(2023, 1, 5, 10, 0, 0)),
            Record(artist: "X", ms: 3_600_000, ts: new DateTime(2023, 1, 20, 10, 0, 0)),
            Record(artist: "X", ms: 3_600_000, ts: new DateTime(2023, 3, 1, 10, 0, 0)),
        };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Artist, "X", string.Empty);

        Assert.Equal(2, d.ByMonth.Count);
        Assert.Equal(new DateTime(2023, 1, 1), d.ByMonth[0].Date);
        Assert.Equal(2.0, d.ByMonth[0].Value);
        Assert.Equal(new DateTime(2023, 3, 1), d.ByMonth[1].Date);
    }

    [Fact]
    public void ByHour_UsesLocalHourOfDay()
    {
        var records = new[]
        {
            Record(artist: "X", ms: 60_000, ts: new DateTime(2023, 5, 1, 14, 0, 0)),
            Record(artist: "X", ms: 60_000, ts: new DateTime(2023, 5, 2, 14, 30, 0)),
        };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Artist, "X", string.Empty);

        Assert.Equal(120_000, d.ByHour[14]);
        Assert.Equal(0, d.ByHour[13]);
    }

    [Fact]
    public void NoMatches_ProducesEmptyResult()
    {
        var records = new[] { Record(artist: "X") };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Artist, "Nobody", string.Empty);

        Assert.Equal(0, d.PlayCount);
        Assert.Empty(d.Tracks);
        Assert.Null(d.FirstPlayed);
    }

    // ---- Spotify links ---------------------------------------------------------------------

    [Fact]
    public void WebUrl_UsesTheTrackUri_WhenTheExportCarriedOne()
    {
        var records = new[] { Record(track: "A", artist: "X", uri: "spotify:track:abc123") };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Track, "A", "X");

        Assert.Equal("https://open.spotify.com/track/abc123", d.WebUrl);
    }

    [Fact]
    public void WebUrl_FallsBackToSearch_WhenThereIsNoUri()
    {
        var records = new[] { Record(track: "A", artist: "X") };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Track, "A", "X");

        Assert.Equal("https://open.spotify.com/search/A%20X", d.WebUrl);
    }

    [Fact]
    public void WebUrl_EscapesNamesWithSpecialCharacters()
    {
        var records = new[] { Record(track: "Q&A / Part #1", artist: "X") };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Track, "Q&A / Part #1", "X");

        Assert.StartsWith("https://open.spotify.com/search/", d.WebUrl);
        Assert.DoesNotContain("/Part", d.WebUrl["https://open.spotify.com/search/".Length..]);
        Assert.DoesNotContain("#", d.WebUrl);
    }

    [Fact]
    public void WebUrl_IgnoresMalformedUris()
    {
        var records = new[] { Record(track: "A", artist: "X", uri: "spotify:track:") };

        var d = DetailEngine.Build(records, NoFilter, DetailScope.Track, "A", "X");

        Assert.StartsWith("https://open.spotify.com/search/", d.WebUrl);
    }
}
