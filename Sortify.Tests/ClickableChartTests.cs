using LiveChartsCore.SkiaSharpView;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>Clicking a bar, a year or a heatmap square.</summary>
[Collection(WpfCollection.Name)]
public class ClickableChartTests
{
    private static AnalysisResult Result()
    {
        PlayRecord Play(string track, int minutes, int year) => new()
        {
            TrackName = track,
            ArtistName = track + " Band",
            AlbumName = track + " LP",
            MsPlayed = minutes * 60_000,
            Timestamp = new DateTime(year, 3, 1, 12, 0, 0),
        };

        return AnalysisEngine.Analyze(new[] { Play("Big", 90, 2023), Play("Middle", 60, 2024), Play("Small", 30, 2024) },
            new FilterOptions());
    }

    // The chart builder reaches the palette, whose brushes come from the WPF resources.
    private static T OnUiThread<T>(Func<T> build)
    {
        T value = default!;
        WpfTestHost.Run(() => value = build());
        return value;
    }

    [Fact]
    public void EachBarKnowsItsRow()
    {
        var chart = OnUiThread(() => ChartBuilder.TopTracksByTime(Result(), 3));

        // Bars are drawn bottom-up, so the biggest is last; its row has to be last too.
        var values = ((RowSeries<double>)chart.Series[0]).Values!.ToList();
        var rows = chart.Items.Cast<TrackStat>().ToList();
        Assert.Equal(new[] { "Small", "Middle", "Big" }, rows.Select(t => t.Track));
        Assert.Equal(rows.Select(t => Math.Round(t.TotalHours, 2)), values);
    }

    [Fact]
    public void EachYearColumnKnowsItsYear()
    {
        var chart = OnUiThread(() => ChartBuilder.HoursPerYear(Result()));

        Assert.Equal(new[] { 2023, 2024 }, chart.Items.Cast<YearStat>().Select(y => y.Year));
    }

    [Fact]
    public void ChartsWithoutRowsHaveNoItems()
    {
        Assert.Empty(OnUiThread(() => ChartBuilder.ByHour(Result())).Items);
    }

    [Fact]
    public void ASkippedTrackOpensAsATrack()
    {
        Assert.Equal((DetailScope.Track, "Song", "Band"),
            DetailWindow.TargetFor(new SkippedTrackStat { Track = "Song", Artist = "Band" }));
    }

    [Fact]
    public void AHeatmapSquare_NarrowsToOneHourOfOneDay()
    {
        var vm = new FilterViewModel();
        int changes = 0;
        vm.FiltersChanged += (_, _) => changes++;

        vm.SetSlot(2, 14);

        Assert.Equal(1, changes);
        Assert.Equal(new[] { "14:00-14:59", "Tue" }, vm.Describe());
        var options = vm.ToOptions();
        Assert.True(options.HourMatches(14));
        Assert.False(options.HourMatches(15));
    }
}
