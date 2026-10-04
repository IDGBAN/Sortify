using LiveChartsCore.SkiaSharpView;
using Sortify.Models;
using Sortify.Services;
using Xunit;
using LcDateTimePoint = LiveChartsCore.Defaults.DateTimePoint;

namespace Sortify.Tests;

/// <summary>Several artists' listening over time, side by side.</summary>
public class ArtistComparisonTests
{
    private static PlayRecord Play(string artist, DateTime when, int minutes = 60) => new()
    {
        TrackName = "Song",
        ArtistName = artist,
        AlbumName = "Album",
        MsPlayed = minutes * 60_000,
        Timestamp = when,
    };

    private static readonly PlayRecord[] Records =
    {
        Play("A", new DateTime(2024, 1, 10)),
        Play("A", new DateTime(2024, 1, 20), 30),
        Play("B", new DateTime(2024, 3, 5)),
        Play("A", new DateTime(2024, 4, 1)),
        Play("C", new DateTime(2024, 6, 1)),
        new() { Kind = ContentKind.Podcast, ShowName = "A", EpisodeName = "Ep", MsPlayed = 3_600_000, Timestamp = new DateTime(2024, 5, 1) },
    };

    private static ArtistTimeline Build(FilterOptions filter, ChartBuilder.TimeGranularity granularity, params string[] artists) =>
        ArtistComparison.Build(Records, filter, artists, granularity);

    [Fact]
    public void Months_RunFromFirstToLastWithQuietMonthsAtZero()
    {
        var timeline = Build(new FilterOptions(), ChartBuilder.TimeGranularity.Monthly, "A", "B");

        Assert.Equal(new[] { 1, 2, 3, 4 }, timeline.Buckets.Select(b => b.Month));
        Assert.Equal(new[] { "A", "B" }, timeline.Series.Select(s => s.Artist));
        Assert.Equal(new[] { 1.5, 0, 0, 1 }, timeline.Series[0].Hours);
        Assert.Equal(new[] { 0d, 0, 1, 0 }, timeline.Series[1].Hours);
    }

    [Fact]
    public void Podcasts_OnlyCountWhenTheFilterSaysSo()
    {
        var without = Build(new FilterOptions(), ChartBuilder.TimeGranularity.Monthly, "A");
        var with = Build(new FilterOptions { IncludePodcasts = true }, ChartBuilder.TimeGranularity.Monthly, "A");

        Assert.Equal(2.5, without.Series[0].Hours.Sum());
        Assert.Equal(3.5, with.Series[0].Hours.Sum());
    }

    [Fact]
    public void Filters_StillApply()
    {
        var timeline = Build(new FilterOptions { StartDate = new DateTime(2024, 3, 1) }, ChartBuilder.TimeGranularity.Monthly, "A");

        Assert.Equal(new DateTime(2024, 4, 1), Assert.Single(timeline.Buckets));
    }

    [Fact]
    public void Weeks_StartOnSunday()
    {
        var timeline = Build(new FilterOptions(), ChartBuilder.TimeGranularity.Weekly, "B");

        // 2024-03-05 was a Tuesday.
        Assert.Equal(new DateTime(2024, 3, 3), Assert.Single(timeline.Buckets));
    }

    [Fact]
    public void AnArtistWithNoPlays_GetsAnEmptyLine()
    {
        var timeline = Build(new FilterOptions(), ChartBuilder.TimeGranularity.Monthly, "Nobody");

        Assert.Empty(timeline.Buckets);
        Assert.Equal("Nobody", Assert.Single(timeline.Series).Artist);
    }

    [Fact]
    public void NoArtists_NoTimeline()
    {
        Assert.Same(ArtistTimeline.Empty, Build(new FilterOptions(), ChartBuilder.TimeGranularity.Monthly));
    }
}

[Collection(WpfCollection.Name)]
public class ArtistComparisonChartTests
{
    [Fact]
    public void EachArtistIsALine()
    {
        ChartData chart = ChartData.Empty;
        var timeline = new ArtistTimeline
        {
            Buckets = new[] { new DateTime(2024, 1, 1), new DateTime(2024, 2, 1) },
            Series = new[] { ("A", new[] { 1.0, 2.0 }), ("B", new[] { 0.0, 3.0 }) },
        };

        WpfTestHost.Run(() => chart = ChartBuilder.ArtistTimelines(timeline, ChartBuilder.TimeGranularity.Monthly));

        Assert.Equal(new[] { "A", "B" }, chart.Series.Select(s => s.Name));
        var b = (LineSeries<LcDateTimePoint>)chart.Series[1];
        Assert.Equal(new double?[] { 0.0, 3.0 }, b.Values!.Select(p => p.Value));
    }
}
