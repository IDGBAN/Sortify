using LiveChartsCore.SkiaSharpView;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>Where the top artists ranked in each year.</summary>
public class RankHistoryTests
{
    private static PlayRecord Play(string artist, int year, int minutes) => new()
    {
        TrackName = artist + " Song",
        ArtistName = artist,
        AlbumName = artist + " LP",
        MsPlayed = minutes * 60_000,
        Timestamp = new DateTime(year, 5, 1, 12, 0, 0),
    };

    internal static AnalysisResult ThreeArtists() => AnalysisEngine.Analyze(new[]
    {
        Play("Steady", 2023, 100), Play("Steady", 2024, 100),
        Play("Riser", 2023, 10), Play("Riser", 2024, 300),
        Play("Faded", 2023, 150),
    }, new FilterOptions());

    [Fact]
    public void EachTopArtist_GetsTheirRankInEveryYear()
    {
        var ranks = ThreeArtists().RankHistory;

        // Ordered by time overall: Riser 310 minutes, Steady 200, Faded 150.
        Assert.Equal(new[] { "Riser", "Steady", "Faded" }, ranks.Select(a => a.Artist.Artist));
        Assert.Equal(new int?[] { 3, 1 }, ranks[0].Ranks);
        Assert.Equal(new int?[] { 2, 2 }, ranks[1].Ranks);
        Assert.Equal(new int?[] { 1, null }, ranks[2].Ranks);
    }

    [Fact]
    public void AYearBelowTheFloor_IsAGap()
    {
        var records = Enumerable.Range(0, AnalysisEngine.RankHistoryFloor)
            .Select(i => Play("Filler " + i, 2023, 5))
            .Append(Play("Star", 2023, 1))
            .Append(Play("Star", 2024, 1000))
            .ToList();

        var star = AnalysisEngine.Analyze(records, new FilterOptions()).RankHistory[0];

        Assert.Equal("Star", star.Artist.Artist);
        Assert.Equal(new int?[] { null, 1 }, star.Ranks);
    }

    [Fact]
    public void OnlyTheTopArtistsOverall_AreFollowed()
    {
        int count = AnalysisEngine.RankHistoryArtists + 3;
        var records = Enumerable.Range(0, count)
            .SelectMany(i => new[] { Play("Artist " + i, 2023, 100 - i), Play("Artist " + i, 2024, 100 - i) })
            .ToList();

        var ranks = AnalysisEngine.Analyze(records, new FilterOptions()).RankHistory;

        Assert.Equal(AnalysisEngine.RankHistoryArtists, ranks.Count);
        for (int i = 0; i < ranks.Count; i++)
        {
            Assert.Equal("Artist " + i, ranks[i].Artist.Artist);
            Assert.Equal(new int?[] { i + 1, i + 1 }, ranks[i].Ranks);
        }
    }

    [Fact]
    public void ASingleYear_HasNoRankHistory()
    {
        var r = AnalysisEngine.Analyze(new[] { Play("Only", 2024, 60), Play("Other", 2024, 30) }, new FilterOptions());

        Assert.Empty(r.RankHistory);
    }

    [Fact]
    public void TheRanks_FollowTheFilters()
    {
        var records = new[] { Play("Riser", 2023, 10), Play("Riser", 2024, 300), Play("Steady", 2023, 100) };

        var r = AnalysisEngine.Analyze(records, new FilterOptions { StartDate = new DateTime(2024, 1, 1) });

        Assert.Empty(r.RankHistory);
    }
}

/// <summary>The rank chart on the Years tab.</summary>
[Collection(WpfCollection.Name)]
public class RankHistoryChartTests
{
    [Fact]
    public void EachArtistIsALine_WithTheirArtistBehindIt()
    {
        var result = RankHistoryTests.ThreeArtists();
        ChartData chart = ChartData.Empty;
        WpfTestHost.Run(() => chart = ChartBuilder.RankHistory(result));

        Assert.Equal(new[] { "Riser", "Steady", "Faded" }, chart.Series.Select(s => s.Name));
        Assert.Equal(new double?[] { 1, null }, (double?[])((LineSeries<double?>)chart.Series[2]).Values!);
        Assert.Equal(new[] { "2023", "2024" }, chart.XAxes[0].Labels);

        // A click finds its artist by the index of the series it landed on.
        Assert.Equal(chart.Series.Length, chart.Items.Count);
        Assert.Equal((DetailScope.Artist, "Faded", string.Empty), DetailWindow.TargetFor(chart.Items[2]));
    }

    [Fact]
    public void TheRankAxis_PutsNumberOneAtTheTop_AndStopsAtTheLowestRank()
    {
        ChartData chart = ChartData.Empty;
        WpfTestHost.Run(() => chart = ChartBuilder.RankHistory(RankHistoryTests.ThreeArtists()));

        var y = chart.YAxes[0];
        Assert.True(y.IsInverted);
        Assert.Equal(0.5, y.MinLimit);
        Assert.Equal(3.5, y.MaxLimit);
        Assert.Equal("#1", y.Labeler(1));
        Assert.Equal(string.Empty, y.Labeler(0));
    }

    [Fact]
    public void WithoutRanks_TheChartIsEmpty()
    {
        ChartData chart = ChartData.Empty;
        WpfTestHost.Run(() => chart = ChartBuilder.RankHistory(new AnalysisResult()));

        Assert.Empty(chart.Series);
        Assert.Empty(chart.Items);
    }

    [Fact]
    public async Task TheSampleHistory_FillsTheRankChart()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(new AppSettings());
            vm.Filters.ResetCommand.Execute(null);
            Assert.False(vm.HasRankHistory);

            await vm.OpenSampleCommand.ExecuteAsync(null);

            Assert.True(vm.HasRankHistory);
            Assert.Equal(AnalysisEngine.RankHistoryArtists, vm.RankChart.Series.Length);
            Assert.Contains($"top {AnalysisEngine.RankHistoryArtists} artists", vm.RankHint);
        });
    }
}
