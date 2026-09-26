using Sortify.Models;
using Sortify.ViewModels;
using Xunit;

namespace Sortify.Tests;

/// <summary>Covers the chip descriptions shown above the tabs.</summary>
public class FilterViewModelTests
{
    [Fact]
    public void Describe_IsEmptyForTheDefaults()
    {
        Assert.Empty(new FilterViewModel().Describe());
    }

    [Fact]
    public void Describe_MentionsANonDefaultMinimumDuration()
    {
        var vm = new FilterViewModel { MinSeconds = 30 };

        Assert.Contains("Min 30s", vm.Describe());
    }

    [Fact]
    public void Describe_MentionsPodcastsOnlyWhenCounted()
    {
        Assert.DoesNotContain("Podcasts counted", new FilterViewModel().Describe());
        Assert.Contains("Podcasts counted", new FilterViewModel { IncludePodcasts = true }.Describe());
    }

    [Fact]
    public void Describe_CollapsesAFullDateRangeIntoOneChip()
    {
        var vm = new FilterViewModel
        {
            StartDate = new DateTime(2023, 1, 1),
            EndDate = new DateTime(2023, 12, 31),
        };

        Assert.Equal(new[] { "2023-01-01 to 2023-12-31" }, vm.Describe());
    }

    [Fact]
    public void Describe_HandlesAnOpenEndedDateRange()
    {
        Assert.Contains("From 2023-01-01",
            new FilterViewModel { StartDate = new DateTime(2023, 1, 1) }.Describe());

        Assert.Contains("Until 2023-12-31",
            new FilterViewModel { EndDate = new DateTime(2023, 12, 31) }.Describe());
    }

    [Fact]
    public void Describe_ListsOnlyTheSelectedDays()
    {
        var vm = new FilterViewModel();
        foreach (var day in vm.Days)
            day.IsSelected = day.Label is "Sat" or "Sun";

        Assert.Contains("Sun, Sat", vm.Describe());
    }

    [Fact]
    public void Describe_SaysSoWhenNoDayIsSelected()
    {
        var vm = new FilterViewModel();
        foreach (var day in vm.Days)
            day.IsSelected = false;

        Assert.Contains("No days selected", vm.Describe());
    }

    [Fact]
    public void Describe_CountsExclusions()
    {
        var vm = new FilterViewModel();
        vm.ExcludeArtist("One");
        vm.ExcludeArtist("Two");
        vm.ExcludeTrack("Only");

        Assert.Contains("2 artists excluded", vm.Describe());
        Assert.Contains("1 track excluded", vm.Describe());
    }

    [Fact]
    public void Describe_MentionsANarrowedHourRange()
    {
        var vm = new FilterViewModel { StartHour = 22, EndHour = 2 };

        Assert.Contains("22:00-02:59", vm.Describe());
    }

    [Theory]
    [InlineData("2023-02-01", "2023-01-01", true)]
    [InlineData("2023-01-01", "2023-01-01", false)]
    [InlineData("2023-01-01", "2023-02-01", false)]
    [InlineData("2023-02-01", null, false)]
    public void HasInvalidDateRange_OnlyWhenFromIsAfterTo(string from, string? to, bool expected)
    {
        var vm = new FilterViewModel
        {
            StartDate = DateTime.Parse(from, System.Globalization.CultureInfo.InvariantCulture),
            EndDate = to is null ? null : DateTime.Parse(to, System.Globalization.CultureInfo.InvariantCulture),
        };

        Assert.Equal(expected, vm.HasInvalidDateRange);
    }

    [Fact]
    public void HasInvalidDateRange_RaisesChangeNotifications()
    {
        var vm = new FilterViewModel { StartDate = new DateTime(2023, 2, 1) };
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.EndDate = new DateTime(2023, 1, 1);

        Assert.Contains(nameof(FilterViewModel.HasInvalidDateRange), changed);
    }

    [Fact]
    public void Reset_ClearsEveryChip()
    {
        var vm = new FilterViewModel { MinSeconds = 60, IncludePodcasts = true, SearchTerm = "x" };
        vm.ExcludeArtist("Someone");

        vm.ResetCommand.Execute(null);

        Assert.Empty(vm.Describe());
        Assert.Equal(FilterOptions.DefaultMinMs / 1000, vm.MinSeconds);
    }
}
