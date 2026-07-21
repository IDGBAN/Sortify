using Sortify.Models;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

public class FilterEngineTests
{
    private static PlayRecord Record(
        string track = "Track", string artist = "Artist", string album = "Album",
        int ms = 60_000, DateTime? ts = null)
        => new()
        {
            TrackName = track,
            ArtistName = artist,
            AlbumName = album,
            MsPlayed = ms,
            Timestamp = ts ?? new DateTime(2023, 5, 10, 14, 0, 0),
        };

    [Fact]
    public void MinDuration_DropsShortPlays()
    {
        var records = new[] { Record(ms: 4_999), Record(ms: 5_000) };
        var filter = new FilterOptions { MinMsPlayed = 5_000 };

        var kept = FilterEngine.Apply(records, filter).ToList();

        Assert.Single(kept);
        Assert.Equal(5_000, kept[0].MsPlayed);
    }

    [Fact]
    public void MinDuration_IgnoredWhenRelaxed()
    {
        var records = new[] { Record(ms: 100) };
        var filter = new FilterOptions { MinMsPlayed = 5_000 };

        Assert.Single(FilterEngine.Apply(records, filter, ignoreMinDuration: true));
    }

    [Fact]
    public void DateRange_IsInclusive()
    {
        var inside = Record(ts: new DateTime(2023, 5, 10));
        var before = Record(ts: new DateTime(2023, 5, 9, 23, 59, 59));
        var after = Record(ts: new DateTime(2023, 5, 11, 0, 0, 1));
        var filter = new FilterOptions
        {
            MinMsPlayed = 0,
            StartDate = new DateTime(2023, 5, 10),
            EndDate = new DateTime(2023, 5, 11),
        };

        var kept = FilterEngine.Apply(new[] { inside, before, after }, filter).ToList();

        Assert.Single(kept);
        Assert.Equal(inside.Timestamp, kept[0].Timestamp);
    }

    [Fact]
    public void ExcludedArtist_IsCaseInsensitive()
    {
        var records = new[] { Record(artist: "Daft Punk"), Record(artist: "Queen") };
        var filter = new FilterOptions { MinMsPlayed = 0 };
        filter.ExcludedArtists.Add("daft punk");

        var kept = FilterEngine.Apply(records, filter).ToList();

        Assert.Single(kept);
        Assert.Equal("Queen", kept[0].ArtistName);
    }

    [Fact]
    public void ExcludedTrack_IsCaseInsensitive()
    {
        var records = new[] { Record(track: "One More Time"), Record(track: "Other") };
        var filter = new FilterOptions { MinMsPlayed = 0 };
        filter.ExcludedTracks.Add("ONE MORE TIME");

        var kept = FilterEngine.Apply(records, filter).ToList();

        Assert.Single(kept);
        Assert.Equal("Other", kept[0].TrackName);
    }

    [Theory]
    [InlineData(23, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(12, false)]
    [InlineData(21, false)]
    public void HourRange_WrapsPastMidnight(int hour, bool expected)
    {
        var record = Record(ts: new DateTime(2023, 5, 10, hour, 30, 0));
        var filter = new FilterOptions { MinMsPlayed = 0, StartHour = 22, EndHour = 2 };

        Assert.Equal(expected, FilterEngine.Apply(new[] { record }, filter).Any());
    }

    [Fact]
    public void DayOfWeek_FiltersOutUncheckedDays()
    {
        // 2023-05-10 is a Wednesday (index 3).
        var wednesday = Record(ts: new DateTime(2023, 5, 10, 12, 0, 0));
        var filter = new FilterOptions { MinMsPlayed = 0 };
        filter.IncludedDaysOfWeek[3] = false;

        Assert.Empty(FilterEngine.Apply(new[] { wednesday }, filter));

        filter.IncludedDaysOfWeek[3] = true;
        Assert.Single(FilterEngine.Apply(new[] { wednesday }, filter));
    }

    [Fact]
    public void Search_MatchesTrackArtistOrAlbum()
    {
        var byTrack = Record(track: "Sunset Drive");
        var byArtist = Record(artist: "Sunset Collective");
        var byAlbum = Record(album: "Sunsets Forever");
        var noMatch = Record();
        var filter = new FilterOptions { MinMsPlayed = 0, SearchTerm = "  sunset  " };

        var kept = FilterEngine.Apply(new[] { byTrack, byArtist, byAlbum, noMatch }, filter).ToList();

        Assert.Equal(3, kept.Count);
    }

    [Fact]
    public void RecordsWithoutTimestamp_BypassTimeOfDayFilters()
    {
        var untimed = Record(ts: DateTime.MinValue);
        var filter = new FilterOptions { MinMsPlayed = 0, StartHour = 9, EndHour = 10 };

        Assert.Single(FilterEngine.Apply(new[] { untimed }, filter));
    }
}
