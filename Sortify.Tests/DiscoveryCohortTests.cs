using LiveChartsCore.SkiaSharpView;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Xunit;

namespace Sortify.Tests;

/// <summary>Each year's listening split by when its music was first heard.</summary>
public class DiscoveryCohortTests
{
    private static PlayRecord Play(string track, DateTime ts, int ms = 3_600_000, string artist = "Band") => new()
    {
        TrackName = track,
        ArtistName = artist,
        AlbumName = "Album",
        MsPlayed = ms,
        Timestamp = ts,
    };

    private static readonly DateTime In2023 = new(2023, 6, 1, 12, 0, 0);
    private static readonly DateTime In2024 = new(2024, 6, 1, 12, 0, 0);

    private static List<PlayRecord> TwoYears() => new()
    {
        Play("Old", In2023),
        Play("Old", In2024),
        Play("Old", In2024.AddDays(1)),
        Play("New", In2024.AddDays(2)),
    };

    [Fact]
    public void EachYear_IsSplitByTheYearItsTracksWereFirstHeard()
    {
        var c = DiscoveryCohorts.Build(TwoYears(), new FilterOptions());

        Assert.Equal(new[] { 2023, 2024 }, c.Years);
        Assert.Equal(new[] { 2023, 2024 }, c.Cohorts);
        Assert.Equal(1, c.Hours[0, 0]);
        Assert.Equal(2, c.Hours[1, 0]);
        Assert.Equal(1, c.Hours[1, 1]);
        Assert.Equal(100.0 / 3, c.NewSharePercent(2024), precision: 6);
        Assert.Equal(200.0 / 3, c.SharePercent(2024, 2023), precision: 6);
    }

    [Fact]
    public void FirstHearing_IsJudgedAgainstTheWholeHistory_WhateverTheDates()
    {
        var c = DiscoveryCohorts.Build(TwoYears(), new FilterOptions { StartDate = new DateTime(2024, 1, 1) });

        Assert.Equal(new[] { 2024 }, c.Years);
        Assert.Equal(new[] { 2023, 2024 }, c.Cohorts);
        Assert.Equal(100.0 / 3, c.NewSharePercent(2024), precision: 6);
    }

    [Fact]
    public void AShortPlay_DoesNotCountAsHearingATrack()
    {
        var records = new List<PlayRecord> { Play("Track", In2023, ms: 2_000), Play("Track", In2024) };

        var c = DiscoveryCohorts.Build(records, new FilterOptions());

        Assert.Equal(new[] { 2024 }, c.Cohorts);
        Assert.Equal(100, c.NewSharePercent(2024));
    }

    [Fact]
    public void Podcasts_OnlyCountWhenTheSidebarSaysSo()
    {
        var records = new List<PlayRecord>
        {
            Play("Song", In2024),
            new() { Kind = ContentKind.Podcast, ShowName = "Show", EpisodeName = "1", MsPlayed = 3_600_000, Timestamp = In2024 },
        };

        Assert.Equal(1, DiscoveryCohorts.Build(records, new FilterOptions()).Hours[0, 0]);
        Assert.Equal(2, DiscoveryCohorts.Build(records, new FilterOptions { IncludePodcasts = true }).Hours[0, 0]);
    }

    [Fact]
    public void NoDatedPlays_GiveAnEmptyBreakdown()
    {
        var c = DiscoveryCohorts.Build(new[] { Play("Undated", DateTime.MinValue) }, new FilterOptions());

        Assert.Empty(c.Years);
        Assert.Equal(0, c.NewSharePercent(2024));
    }

    [Fact]
    public void TheSentence_GivesTheLatestYearsShareOfNewMusic()
    {
        var c = DiscoveryCohorts.Build(TwoYears(), new FilterOptions());

        Assert.Equal("33% of 2024's listening was music you first heard that year, and 67% was music found in 2023. "
            + "The first year of a history is all new by definition.", InsightText.Cohorts(c));
    }

    [Fact]
    public void ByPlays_TheSplitAndTheSentenceCountPlays()
    {
        // In 2024 the old track is played twice for an hour and the new one ten times for a minute.
        var records = new List<PlayRecord> { Play("Old", In2023), Play("Old", In2024), Play("Old", In2024.AddDays(1)) };
        records.AddRange(Enumerable.Range(0, 10).Select(i => Play("New", In2024.AddDays(2 + i), ms: 60_000)));

        var c = DiscoveryCohorts.Build(records, new FilterOptions());

        Assert.Equal(2, c.Plays[1, 0]);
        Assert.Equal(10, c.Plays[1, 1]);
        Assert.Equal(1000.0 / 12, c.NewSharePercent(2024, Metric.Plays), precision: 6);
        Assert.True(c.NewSharePercent(2024) < 10);
        Assert.Equal("83% of 2024's plays were of music you first heard that year, and 17% of music found in 2023. "
            + "The first year of a history is all new by definition.", InsightText.Cohorts(c, Metric.Plays));
    }

    [Fact]
    public void TheSentence_ForASingleYear_SaysEverythingIsNew()
    {
        var c = DiscoveryCohorts.Build(new[] { Play("Only", In2024) }, new FilterOptions());

        Assert.Equal("With only 2024 in the history, all of it counts as music first heard that year.", InsightText.Cohorts(c));
        Assert.Equal(string.Empty, InsightText.Cohorts(CohortBreakdown.Empty));
    }
}

/// <summary>The stacked chart and the toggle on the Years tab.</summary>
[Collection(WpfCollection.Name)]
public class DiscoveryCohortChartTests
{
    [Fact]
    public void TheChart_BandsYearsBack_AndFoldsOlderFinds()
    {
        int back = DiscoveryCohorts.YearsBack;
        int first = 2010, last = first + back + 2;
        var records = Enumerable.Range(first, last - first + 1)
            .Select(y => new PlayRecord
            {
                TrackName = "Since " + first,
                ArtistName = "Band",
                AlbumName = "Album",
                MsPlayed = 3_600_000,
                Timestamp = new DateTime(y, 3, 1),
            })
            .ToList();
        var cohorts = DiscoveryCohorts.Build(records, new FilterOptions());
        var years = AnalysisEngine.Analyze(records, new FilterOptions()).Years;

        ChartData chart = ChartData.Empty;
        WpfTestHost.Run(() => chart = ChartBuilder.Cohorts(cohorts, years));

        var names = chart.Series.Select(s => s.Name).ToList();
        Assert.Equal("Found that year", names[0]);
        Assert.Equal("Found earlier", names[^1]);

        // The one track was found in the first year, so the last year's hour is in the folded band.
        var earlier = (double[])((StackedColumnSeries<double>)chart.Series[^1]).Values!;
        Assert.Equal(1, earlier[^1]);
        Assert.Equal(0, earlier[0]);
        Assert.Equal(last, ((YearStat)chart.Items[^1]).Year);
    }

    [Fact]
    public async Task TurningTheViewOn_WorksOutTheBreakdown()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(new AppSettings());
            vm.Filters.ResetCommand.Execute(null);
            await vm.OpenSampleCommand.ExecuteAsync(null);
            Assert.Empty(vm.CohortChart.Series);

            vm.ShowCohorts = true;
            await Wait.UntilAsync(() => vm.CohortChart.Series.Length > 0);

            Assert.NotEmpty(vm.CohortChart.Series);
            Assert.Contains("first heard that year", vm.CohortText);
        });
    }
}
