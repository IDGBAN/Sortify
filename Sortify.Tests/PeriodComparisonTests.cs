using System.Collections;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Xunit;

namespace Sortify.Tests;

/// <summary>Two date ranges of one history, side by side.</summary>
public class PeriodComparisonTests
{
    private static PlayRecord Play(string track, string artist, int minutes, DateTime when) => new()
    {
        TrackName = track,
        ArtistName = artist,
        AlbumName = "Album",
        MsPlayed = minutes * 60_000,
        Timestamp = when,
    };

    private static readonly DateTime A = new(2023, 6, 1, 12, 0, 0);
    private static readonly DateTime B = new(2024, 6, 1, 12, 0, 0);

    private static readonly List<PlayRecord> Records = new()
    {
        Play("Rising", "Riser", 30, A),
        Play("Big", "Steady", 300, A),
        Play("Old Song", "Faded", 120, A),
        Play("Rising", "Riser", 400, B),
        Play("Big", "Steady", 200, B),
        Play("Fresh", "Newcomer", 90, B),
        Play("Blip", "Brief", 10, B),
    };

    private static Task<PeriodComparisonResult> Compare(FilterOptions? filter = null) =>
        PeriodComparison.CompareAsync(Records, filter ?? new FilterOptions(),
            new DateTime(2023, 1, 1), new DateTime(2023, 12, 31),
            new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));

    [Fact]
    public async Task Artists_ShowTheirRankInEachPeriodAndHowTheyMoved()
    {
        var result = await Compare();

        var riser = result.Artists.Single(r => r.Name == "Riser");
        Assert.Equal((3, 1), (riser.RankA, riser.RankB));
        Assert.Equal(Trend.Up, riser.Trend);
        Assert.Equal("▲ 2", riser.Movement);

        var steady = result.Artists.Single(r => r.Name == "Steady");
        Assert.Equal("▼ 1", steady.Movement);

        Assert.Equal("new", result.Artists.Single(r => r.Name == "Newcomer").Movement);
        Assert.Equal("out", result.Artists.Single(r => r.Name == "Faded").Movement);
    }

    [Fact]
    public void AChange_IsColouredAsItReads()
    {
        var a = new AnalysisResult { TotalPlays = 1000, TotalMsPlayed = 1000, ActiveDays = 200, SkipEligiblePlays = 1000, TotalSkips = 250 };
        var b = new AnalysisResult { TotalPlays = 997, TotalMsPlayed = 995, ActiveDays = 200, SkipEligiblePlays = 1000, TotalSkips = 251 };

        var metrics = PeriodComparison.Build(a, b).Metrics.ToDictionary(m => m.Metric);

        Assert.Equal(("0%", Trend.Same), (metrics["Plays"].Change, metrics["Plays"].Trend));
        Assert.Equal(("-1%", Trend.Down), (metrics["Listening time"].Change, metrics["Listening time"].Trend));
        Assert.Equal(("+0.1 points", Trend.Up), (metrics["Skip rate"].Change, metrics["Skip rate"].Trend));
    }

    [Fact]
    public void ASkipRateUpOnePoint_SaysPoint()
    {
        var a = new AnalysisResult { SkipEligiblePlays = 100, TotalSkips = 20 };
        var b = new AnalysisResult { SkipEligiblePlays = 100, TotalSkips = 21 };

        Assert.Equal("+1 point", PeriodComparison.Build(a, b).Metrics.Single(m => m.Metric == "Skip rate").Change);
    }

    [Fact]
    public async Task APeriodEndingOnTheLastDayOfTheCalendar_StillCompares()
    {
        var result = await PeriodComparison.CompareAsync(Records, new FilterOptions(),
            new DateTime(2023, 1, 1), new DateTime(2023, 12, 31), new DateTime(2024, 1, 1), DateTime.MaxValue.Date);

        Assert.Equal(4, result.B.TotalPlays);
    }

    [Fact]
    public async Task Artists_AreOrderedByPeriodBWithDropoutsLast()
    {
        var result = await Compare();

        Assert.Equal(new[] { "Riser", "Steady", "Newcomer", "Brief", "Faded" }, result.Artists.Select(r => r.Name));
    }

    [Fact]
    public async Task Tracks_KeepTheirArtist()
    {
        var result = await Compare();

        var rising = result.Tracks.First();
        Assert.Equal(("Rising", "Riser"), (rising.Name, rising.Secondary));
        Assert.Equal(400, rising.HoursB * 60, 3);
    }

    [Fact]
    public async Task NewAndGone_NeedAnHourOfListening()
    {
        var result = await Compare();

        Assert.Equal(new[] { "Newcomer" }, result.NewInB.Select(r => r.Name));
        Assert.Equal(new[] { "Faded" }, result.GoneSinceA.Select(r => r.Name));
    }

    [Fact]
    public async Task TheLastDayOfAPeriodCountsInFull()
    {
        var late = new List<PlayRecord>(Records) { Play("Late", "Night Owl", 60, new DateTime(2024, 12, 31, 23, 30, 0)) };

        var result = await PeriodComparison.CompareAsync(late, new FilterOptions(),
            null, new DateTime(2023, 12, 31), new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));

        Assert.Contains(result.NewInB, r => r.Name == "Night Owl");
    }

    [Fact]
    public async Task SidebarFiltersApplyToBothPeriods()
    {
        var filter = new FilterOptions();
        filter.ExcludedArtists.Add("Riser");

        var result = await Compare(filter);

        Assert.DoesNotContain(result.Artists, r => r.Name == "Riser");
        Assert.Equal(3, result.B.UniqueArtists);
    }

    [Fact]
    public async Task Metrics_SayHowTheTotalsChanged()
    {
        var result = await Compare();
        var metrics = result.Metrics.ToDictionary(m => m.Metric);

        // A: 450 minutes, B: 700 minutes.
        Assert.Equal("+56%", metrics["Listening time"].Change);
        Assert.Equal(Trend.Up, metrics["Listening time"].Trend);
        Assert.Equal("+33%", metrics["Plays"].Change);
        Assert.Equal(("Steady", "Riser", "changed"), (metrics["Top artist"].A, metrics["Top artist"].B, metrics["Top artist"].Change));
    }

    [Fact]
    public void Metrics_CallSomethingFromNothingNew()
    {
        var result = PeriodComparison.Build(AnalysisResult.Empty, AnalysisEngine.Analyze(Records, new FilterOptions()));

        Assert.Equal("new", result.Metrics.Single(m => m.Metric == "Plays").Change);
    }

    [Fact]
    public void WithDateRange_CopiesEveryOtherFilter()
    {
        var original = new FilterOptions
        {
            MinMsPlayed = 1234,
            IncludePodcasts = true,
            StartDate = new DateTime(2020, 1, 1),
            EndDate = new DateTime(2020, 2, 1),
            SearchTerm = "x",
            StartHour = 3,
            EndHour = 4,
            Shuffle = PlaybackMode.Only,
            Offline = PlaybackMode.Exclude,
            Private = PlaybackMode.Exclude,
        };
        original.ExcludedArtists.Add("a");
        original.ExcludedTracks.Add("t");
        original.ExcludedDevices.Add("Car");
        original.ExcludedCountries.Add("US");
        original.IncludedDaysOfWeek[2] = false;

        var copy = original.WithDateRange(new DateTime(2024, 1, 1), null);

        Assert.Equal(new DateTime(2024, 1, 1), copy.StartDate);
        Assert.Null(copy.EndDate);
        // Every other property, including any added later, has to come across.
        foreach (var property in typeof(FilterOptions).GetProperties())
        {
            if (property.Name is nameof(FilterOptions.StartDate) or nameof(FilterOptions.EndDate))
                continue;
            var a = property.GetValue(original);
            var b = property.GetValue(copy);
            if (a is IEnumerable sequence and not string)
                Assert.Equal(sequence.Cast<object>(), ((IEnumerable)b!).Cast<object>());
            else
                Assert.Equal(a, b);
        }
    }
}

/// <summary>The Compare tab's own behaviour.</summary>
public class CompareViewModelTests
{
    private static readonly DateTime LastListen = new(2026, 6, 29, 19, 0, 0);

    private static CompareViewModel Model(Func<CompareViewModel.Source?>? source = null)
    {
        var vm = new CompareViewModel(source ?? (() => null));
        vm.SetHistory(LastListen, new[] { 2024, 2025, 2026 }, resetPeriods: true);
        return vm;
    }

    [Fact]
    public void StartsWithTheLastTwelveMonthsAgainstTheTwelveBefore()
    {
        var vm = Model();

        Assert.Equal((new DateTime(2025, 6, 30), new DateTime(2026, 6, 29)), (vm.StartB, vm.EndB));
        Assert.Equal((new DateTime(2024, 6, 30), new DateTime(2025, 6, 29)), (vm.StartA, vm.EndA));
        Assert.Equal("Period B: 2025-06-30 to 2026-06-29", vm.PeriodBLabel);
    }

    [Fact]
    public void ReloadingKeepsThePeriodsTheUserChose()
    {
        var vm = Model();
        vm.StartB = new DateTime(2025, 1, 1);

        vm.SetHistory(LastListen, new[] { 2024, 2025, 2026 }, resetPeriods: false);

        Assert.Equal(new DateTime(2025, 1, 1), vm.StartB);
    }

    [Fact]
    public void Now_EndsJustThatPeriodToday()
    {
        var vm = Model();
        var (startA, endA, startB) = (vm.StartA, vm.EndA, vm.StartB);

        vm.EndBNowCommand.Execute(null);

        Assert.Equal((startB, DateTime.Today), (vm.StartB, vm.EndB));
        Assert.Equal((startA, endA), (vm.StartA, vm.EndA));
        Assert.Equal($"Period B: 2025-06-30 to {TimeFormat.Day(DateTime.Today)}", vm.PeriodBLabel);

        vm.EndANowCommand.Execute(null);

        Assert.Equal((startA, DateTime.Today), (vm.StartA, vm.EndA));
    }

    [Fact]
    public void SameDaysInOtherYears_MovesTheOtherPeriodsMonthsAndDaysNewestYearFirst()
    {
        var vm = Model();
        vm.StartB = new DateTime(2026, 1, 1);
        vm.EndB = new DateTime(2026, 6, 29);

        var options = vm.SameDaysInOtherYears(forA: true);

        Assert.Equal(new[] { "2025", "2024" }, options.Select(o => o.Label));
        Assert.Equal((new DateTime(2025, 1, 1), new DateTime(2025, 6, 29)), (options[0].Start, options[0].End));
        Assert.Equal("2024-01-01 to 2024-06-29", options[1].Tooltip);

        vm.ApplyRangeACommand.Execute(options[0]);

        Assert.Equal((new DateTime(2025, 1, 1), new DateTime(2025, 6, 29)), (vm.StartA, vm.EndA));
        Assert.Equal((new DateTime(2026, 1, 1), new DateTime(2026, 6, 29)), (vm.StartB, vm.EndB));
    }

    [Fact]
    public void SameDays_DropdownSetsThePeriodAndShowsTheYearInUse()
    {
        var vm = Model();
        vm.StartB = new DateTime(2026, 1, 1);
        vm.EndB = new DateTime(2026, 6, 29);

        Assert.True(vm.HasSameDaysA);
        Assert.Equal(new[] { "2025", "2024" }, vm.SameDaysA.Select(o => o.Label));

        vm.SelectedSameDaysA = vm.SameDaysA.Single(o => o.Label == "2025");

        Assert.Equal((new DateTime(2025, 1, 1), new DateTime(2025, 6, 29)), (vm.StartA, vm.EndA));
        Assert.Equal(new[] { "2025" }, vm.SameDaysA.Where(o => o.IsActive).Select(o => o.Label));
        Assert.Equal("2025", vm.SelectedSameDaysA?.Label);

        vm.StartA = new DateTime(2025, 1, 2);
        Assert.Null(vm.SelectedSameDaysA);

        vm.EndB = null;
        Assert.False(vm.HasSameDaysA);
    }

    [Fact]
    public void SameDaysInOtherYears_WorksFromAToBToo()
    {
        var vm = Model();
        vm.StartA = new DateTime(2024, 3, 10);
        vm.EndA = new DateTime(2024, 4, 20);

        var options = vm.SameDaysInOtherYears(forA: false);

        Assert.Equal(new[] { "2026", "2025" }, options.Select(o => o.Label));
        Assert.Equal((new DateTime(2026, 3, 10), new DateTime(2026, 4, 20)), (options[0].Start, options[0].End));
    }

    [Fact]
    public void SameDaysInOtherYears_NamesAStretchAcrossNewYearByTheYearItEndsIn()
    {
        var vm = Model();

        var options = vm.SameDaysInOtherYears(forA: true);

        Assert.Equal(new[] { "2025", "2024" }, options.Select(o => o.Label));
        Assert.Equal((new DateTime(2024, 6, 30), new DateTime(2025, 6, 29)), (options[0].Start, options[0].End));
    }

    [Theory]
    [InlineData("2025-02-01", "2025-02-28", "2024", "2024-02-01", "2024-02-29")]
    [InlineData("2024-02-01", "2024-02-29", "2025", "2025-02-01", "2025-02-28")]
    [InlineData("2024-02-10", "2024-02-29", "2026", "2026-02-10", "2026-02-28")]
    public void SameDaysInOtherYears_KeepsAMonthsEndAtTheMonthsEnd(string start, string end, string year, string newStart, string newEnd)
    {
        var vm = Model();
        vm.StartB = DateTime.Parse(start, System.Globalization.CultureInfo.InvariantCulture);
        vm.EndB = DateTime.Parse(end, System.Globalization.CultureInfo.InvariantCulture);

        var option = vm.SameDaysInOtherYears(forA: true).Single(o => o.Label == year);

        Assert.Equal(DateTime.Parse(newStart, System.Globalization.CultureInfo.InvariantCulture), option.Start);
        Assert.Equal(DateTime.Parse(newEnd, System.Globalization.CultureInfo.InvariantCulture), option.End);
    }

    [Fact]
    public void SameDaysInOtherYears_NeedsBothEndsOfTheOtherPeriod()
    {
        var vm = Model();
        vm.EndB = null;

        Assert.Empty(vm.SameDaysInOtherYears(forA: true));
    }

    [Fact]
    public void Swap_TradesThePeriods()
    {
        var vm = Model();
        var (a, b) = ((vm.StartA, vm.EndA), (vm.StartB, vm.EndB));

        vm.SwapCommand.Execute(null);

        Assert.Equal(b, (vm.StartA, vm.EndA));
        Assert.Equal(a, (vm.StartB, vm.EndB));
    }

    [Fact]
    public void MatchAToB_TakesTheSameNumberOfDaysJustBefore()
    {
        var vm = Model();
        vm.StartB = new DateTime(2026, 5, 31);
        vm.EndB = new DateTime(2026, 6, 29);

        vm.MatchAToBCommand.Execute(null);

        Assert.Equal((new DateTime(2026, 5, 1), new DateTime(2026, 5, 30)), (vm.StartA, vm.EndA));
        Assert.Equal((vm.EndB - vm.StartB), (vm.EndA - vm.StartA));
    }

    [Theory]
    [InlineData("2025-01-01", "2025-12-31", "2024-01-01", "2024-12-31")]
    [InlineData("2024-03-01", "2024-03-31", "2024-02-01", "2024-02-29")]
    [InlineData("2025-04-01", "2025-06-30", "2025-01-01", "2025-03-31")]
    public void MatchAToB_KeepsWholeMonthsWhole(string startB, string endB, string startA, string endA)
    {
        var vm = Model();
        vm.StartB = DateTime.Parse(startB, System.Globalization.CultureInfo.InvariantCulture);
        vm.EndB = DateTime.Parse(endB, System.Globalization.CultureInfo.InvariantCulture);

        vm.MatchAToBCommand.Execute(null);

        Assert.Equal(DateTime.Parse(startA, System.Globalization.CultureInfo.InvariantCulture), vm.StartA);
        Assert.Equal(DateTime.Parse(endA, System.Globalization.CultureInfo.InvariantCulture), vm.EndA);
    }

    [Theory]
    [InlineData("2024-01-01")]
    [InlineData("2024-01-02")]
    public void MatchAToB_SaysSoWhenBRunsToTheEndOfTheCalendar(string startB)
    {
        var vm = Model();
        var before = (vm.StartA, vm.EndA);
        vm.StartB = DateTime.Parse(startB, System.Globalization.CultureInfo.InvariantCulture);
        vm.EndB = DateTime.MaxValue.Date;

        vm.MatchAToBCommand.Execute(null);

        Assert.Equal(before, (vm.StartA, vm.EndA));
        Assert.Contains("no room before period B", vm.Message);
    }

    [Fact]
    public void MatchAToB_NeedsBothEndsOfB()
    {
        var vm = Model();
        vm.StartB = null;
        vm.EndB = null;

        vm.MatchAToBCommand.Execute(null);

        Assert.Contains("Give period B a start and an end", vm.Message);
    }

    [Fact]
    public async Task ABackwardsPeriod_IsExplainedInsteadOfCompared()
    {
        var vm = Model(() => new CompareViewModel.Source(Array.Empty<PlayRecord>(), new FilterOptions(), TimeSpan.FromMinutes(30)));
        vm.EndA = new DateTime(2020, 1, 1);

        await vm.RefreshAsync();

        Assert.Contains("starts after it ends", vm.Message);
        Assert.Empty(vm.Metrics);
    }

    [Fact]
    public async Task ABackwardsPeriod_StopsTheComparisonAlreadyRunning()
    {
        // On the UI thread, so the first comparison can't finish until the second refresh is done.
        await WpfTestHost.RunAsync(async () =>
        {
            var plays = new List<PlayRecord>
            {
                new() { TrackName = "T", ArtistName = "A", AlbumName = "L", MsPlayed = 600_000, Timestamp = new DateTime(2026, 1, 5, 12, 0, 0) },
            };
            var vm = Model(() => new CompareViewModel.Source(plays, new FilterOptions(), TimeSpan.FromMinutes(30)));

            var comparing = vm.RefreshAsync();
            vm.EndA = new DateTime(2020, 1, 1);
            await vm.RefreshAsync();
            await comparing;

            Assert.Contains("starts after it ends", vm.Message);
            Assert.Empty(vm.Metrics);
            Assert.False(vm.IsWorking);
        });
    }

    [Fact]
    public async Task NothingIsWorkedOutWhileTheTabIsHidden()
    {
        int asked = 0;
        var vm = Model(() =>
        {
            asked++;
            return new CompareViewModel.Source(Array.Empty<PlayRecord>(), new FilterOptions(), TimeSpan.FromMinutes(30));
        });

        vm.Invalidate();
        Assert.Equal(0, asked);

        vm.IsVisible = true;
        await Wait.UntilAsync(() => vm.Message.Length > 0);

        Assert.Equal(1, asked);
        Assert.Equal("Neither period has any plays under the current filters.", vm.Message);
    }
}
