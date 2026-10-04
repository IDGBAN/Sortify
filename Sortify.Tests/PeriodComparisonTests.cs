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
        Assert.True(vm.RangesB.Single(r => r.Label == "Last 12 months").IsActive);
    }

    [Fact]
    public void ReloadingKeepsThePeriodsTheUserChose()
    {
        var vm = Model();
        vm.ApplyRangeBCommand.Execute(vm.RangesB.Single(r => r.Label == "2025"));

        vm.SetHistory(LastListen, new[] { 2024, 2025, 2026 }, resetPeriods: false);

        Assert.Equal(new DateTime(2025, 1, 1), vm.StartB);
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
        vm.ApplyRangeBCommand.Execute(vm.RangesB.Single(r => r.Label == "Last 30 days"));

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

    [Fact]
    public void MatchAToB_NeedsBothEndsOfB()
    {
        var vm = Model();
        vm.ApplyRangeBCommand.Execute(vm.RangesB.Single(r => r.Label == "All time"));

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
        await Task.Delay(200);

        Assert.Equal(1, asked);
        Assert.Equal("Neither period has any plays under the current filters.", vm.Message);
    }
}
