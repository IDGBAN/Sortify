using Sortify.ViewModels;
using Xunit;

namespace Sortify.Tests;

/// <summary>The one-click date ranges under the date pickers.</summary>
public class QuickRangeTests
{
    private static readonly DateTime LastListen = new(2025, 6, 29, 21, 15, 0);

    [Fact]
    public void For_CountsBackFromTheLastListenNotFromToday()
    {
        var options = DateRangeOption.For(LastListen, new[] { 2024, 2025 });

        var month = options.Single(o => o.Label == "Last 30 days");
        Assert.Equal(new DateTime(2025, 5, 31), month.Start);
        Assert.Equal(new DateTime(2025, 6, 29), month.End);

        var year = options.Single(o => o.Label == "Last 12 months");
        Assert.Equal(new DateTime(2024, 6, 30), year.Start);
        Assert.Equal(new DateTime(2025, 6, 29), year.End);
    }

    [Fact]
    public void For_OffersEveryYearOnceInOrder()
    {
        var options = DateRangeOption.For(LastListen, new[] { 2025, 2023, 2024, 2023 });

        Assert.Equal(new[] { "All time", "Last 30 days", "Last 12 months", "2023", "2024", "2025" },
            options.Select(o => o.Label));
        var y2024 = options.Single(o => o.Label == "2024");
        Assert.Equal(new DateTime(2024, 1, 1), y2024.Start);
        Assert.Equal(new DateTime(2024, 12, 31), y2024.End);
    }

    [Fact]
    public void For_OffersNothingWithoutDatedPlays()
    {
        Assert.Empty(DateRangeOption.For(null, Array.Empty<int>()));
    }

    [Fact]
    public void Applying_SetsBothDatesInOneChange()
    {
        var vm = new FilterViewModel();
        vm.SetAvailableDates(LastListen, new[] { 2024, 2025 });
        int changes = 0;
        vm.FiltersChanged += (_, _) => changes++;

        vm.ApplyQuickRangeCommand.Execute(vm.QuickRanges.Single(o => o.Label == "2024"));

        Assert.Equal(new DateTime(2024, 1, 1), vm.StartDate);
        Assert.Equal(new DateTime(2024, 12, 31), vm.EndDate);
        Assert.Equal(1, changes);
        Assert.Equal(new[] { "2024-01-01 to 2024-12-31" }, vm.Describe());
    }

    [Fact]
    public void TheRangeInEffectIsMarked_HoweverItWasSet()
    {
        var vm = new FilterViewModel();
        vm.SetAvailableDates(LastListen, new[] { 2024, 2025 });
        Assert.True(vm.QuickRanges.Single(o => o.Label == "All time").IsActive);

        vm.StartDate = new DateTime(2025, 1, 1);
        vm.EndDate = new DateTime(2025, 12, 31);

        Assert.Equal(new[] { "2025" }, vm.QuickRanges.Where(o => o.IsActive).Select(o => o.Label));

        vm.EndDate = new DateTime(2025, 12, 30);

        Assert.DoesNotContain(vm.QuickRanges, o => o.IsActive);
    }

    [Fact]
    public void AllTime_ClearsTheRange()
    {
        var vm = new FilterViewModel { StartDate = new DateTime(2024, 1, 1) };
        vm.SetAvailableDates(LastListen, new[] { 2024 });

        vm.ApplyQuickRangeCommand.Execute(vm.QuickRanges[0]);

        Assert.Null(vm.StartDate);
        Assert.Null(vm.EndDate);
    }
}
