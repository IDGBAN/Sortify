using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>The top artist and track of every month.</summary>
public class MonthlyTopTests
{
    private static PlayRecord Play(string track, string artist, int minutes, int year, int month, int day = 1) => new()
    {
        TrackName = track,
        ArtistName = artist,
        AlbumName = "Album",
        MsPlayed = minutes * 60_000,
        Timestamp = new DateTime(year, month, day, 12, 0, 0),
    };

    [Fact]
    public void EachMonthGetsItsOwnLeaders()
    {
        var result = AnalysisEngine.Analyze(new[]
        {
            Play("Song A", "Band A", 30, 2024, 1),
            Play("Song B", "Band B", 20, 2024, 1),
            Play("Song B2", "Band B", 20, 2024, 1, 2),
            Play("Song C", "Band C", 10, 2024, 2),
        }, new FilterOptions());

        Assert.Equal(new[] { new DateTime(2024, 1, 1), new DateTime(2024, 2, 1) }, result.Months.Select(m => m.Month));
        var january = result.Months[0];
        // Band B leads the month on two tracks, though Song A is its single biggest track.
        Assert.Equal("Band B", january.TopArtist);
        Assert.Equal(40 * 60_000, january.TopArtistMs);
        Assert.Equal("Song A", january.TopTrack);
        Assert.Equal("Band A", january.TopTrackArtist);
        Assert.Equal(3, january.PlayCount);
        Assert.Equal("Band C", result.Months[1].TopArtist);
    }

    [Fact]
    public void YearsStillAgreeWithTheMonths()
    {
        var result = AnalysisEngine.Analyze(new[]
        {
            Play("Song A", "Band A", 30, 2024, 1),
            Play("Song B", "Band B", 50, 2024, 2),
        }, new FilterOptions());

        var year = Assert.Single(result.Years);
        Assert.Equal("Band B", year.TopArtist);
        Assert.Equal("Song B", year.TopTrack);
        Assert.Equal(result.Months.Sum(m => m.TotalMsPlayed), year.TotalMsPlayed);
    }

    private static MonthStat Month(int year, int month, string artist, long ms = 1000) =>
        new() { Month = new DateTime(year, month, 1), TopArtist = artist, TopArtistMs = ms };

    [Fact]
    public void Leader_CountsMonthsAndTheLongestRun()
    {
        var months = new[]
        {
            Month(2024, 1, "A"), Month(2024, 2, "A"), Month(2024, 3, "B"),
            Month(2024, 4, "A"), Month(2024, 5, "A"), Month(2024, 6, "A"),
        };

        Assert.Equal("A was your #1 artist in 5 of 6 months, 3 of them in a row (2024-04 to 2024-06).",
            MainViewModel.DescribeMonthlyLeader(months));
    }

    [Fact]
    public void Leader_RunsBreakOnAGapInTheCalendar()
    {
        // Two months apart are not "in a row", even with nothing listened to in between.
        var months = new[] { Month(2024, 1, "A"), Month(2024, 3, "A") };

        Assert.Equal("A was your #1 artist in 2 of 2 months.", MainViewModel.DescribeMonthlyLeader(months));
    }

    [Fact]
    public void Leader_TiesGoToMoreListening()
    {
        var months = new[] { Month(2024, 1, "A", 100), Month(2024, 2, "B", 900) };

        Assert.StartsWith("B was your #1 artist in 1 of 2 months", MainViewModel.DescribeMonthlyLeader(months));
    }

    [Fact]
    public void Leader_IsBlankWithNoMonths()
    {
        Assert.Equal(string.Empty, MainViewModel.DescribeMonthlyLeader(Array.Empty<MonthStat>()));
    }

    [Fact]
    public void AMonthRowOpensItsTopArtist()
    {
        Assert.Equal((DetailScope.Artist, "A", ""), DetailWindow.TargetFor(Month(2024, 1, "A")));
    }
}
