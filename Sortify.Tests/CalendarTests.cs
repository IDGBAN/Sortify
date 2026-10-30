using System.Windows;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>The listening calendar and the day breakdown it opens.</summary>
public class CalendarTests
{
    private static List<DateTimePoint> Days(params (int Year, int Month, int Day, double Hours)[] days) =>
        days.Select(d => new DateTimePoint(new DateTime(d.Year, d.Month, d.Day), d.Hours)).ToList();

    private static CalendarViewModel Calendar(List<DateTimePoint> days, DayOfWeek first = DayOfWeek.Sunday)
    {
        var vm = new CalendarViewModel();
        vm.Update(days, first);
        return vm;
    }

    [Fact]
    public void ItShowsTheLatestYear_AndOffersEveryYearWithListening()
    {
        var vm = Calendar(Days((2023, 5, 1, 1), (2024, 3, 14, 2)));

        Assert.Equal(2024, vm.Year);
        Assert.Equal(new[] { 2023, 2024 }, vm.Years.Select(y => y.Year));
        Assert.True(vm.Years.Single(y => y.Year == 2024).IsActive);
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, 1)]
    [InlineData(DayOfWeek.Monday, 0)]
    [InlineData(DayOfWeek.Saturday, 2)]
    public void TheFirstColumn_PutsNewYearsDayInItsWeekdaysRow(DayOfWeek first, int row)
    {
        // 1 January 2024 was a Monday.
        var vm = Calendar(Days((2024, 1, 1, 1)), first);
        var firstWeek = vm.Weeks[0].Days;

        Assert.Equal(new DateTime(2024, 1, 1), firstWeek[row].Date);
        Assert.True(firstWeek[row].IsInYear);
        Assert.All(firstWeek.Take(row), d => Assert.False(d.IsInYear));
    }

    [Fact]
    public void TheWeeks_CoverEveryDayOfTheYearOnce()
    {
        var vm = Calendar(Days((2024, 6, 1, 1)));

        var inYear = vm.Weeks.SelectMany(w => w.Days).Where(d => d.IsInYear).Select(d => d.Date).ToList();

        Assert.Equal(366, inYear.Count);
        Assert.Equal(inYear.Distinct().Count(), inYear.Count);
        Assert.All(vm.Weeks, w => Assert.Equal(7, w.Days.Count));
    }

    [Fact]
    public void Shades_SplitTheListeningDaysIntoQuarters()
    {
        var vm = Calendar(Days(
            (2024, 1, 1, 1), (2024, 1, 2, 2), (2024, 1, 3, 3), (2024, 1, 4, 4),
            (2024, 1, 5, 5), (2024, 1, 6, 6), (2024, 1, 7, 7), (2024, 1, 8, 8)));
        var byDate = vm.Weeks.SelectMany(w => w.Days).Where(d => d.IsInYear).ToDictionary(d => d.Date);

        Assert.Equal(1, byDate[new DateTime(2024, 1, 1)].Level);
        Assert.Equal(2, byDate[new DateTime(2024, 1, 4)].Level);
        Assert.Equal(3, byDate[new DateTime(2024, 1, 6)].Level);
        Assert.Equal(4, byDate[new DateTime(2024, 1, 8)].Level);
        Assert.Equal(0, byDate[new DateTime(2024, 1, 9)].Level);
    }

    [Fact]
    public void EveryMonth_IsLabelledAboveItsFirstWeek()
    {
        var vm = Calendar(Days((2024, 6, 1, 1)));

        Assert.Equal(12, vm.MonthLabels.Count);
        Assert.Equal("Jan", vm.MonthLabels[0].Text);
        Assert.Equal(0, vm.MonthLabels[0].Left);
        Assert.True(vm.MonthLabels.Zip(vm.MonthLabels.Skip(1)).All(p => p.Second.Left > p.First.Left));
    }

    [Fact]
    public void EveryOtherDay_IsNamedDownTheSide()
    {
        var vm = Calendar(Days((2024, 6, 1, 1)), DayOfWeek.Monday);

        Assert.Equal(new[] { "", "Tue", "", "Thu", "", "Sat", "" }, vm.DayLabels);
    }

    [Fact]
    public void PickingAYear_ShowsIt_AndANewAnalysisKeepsIt()
    {
        var days = Days((2023, 5, 1, 1), (2024, 3, 14, 2));
        var vm = Calendar(days);

        vm.ShowYearCommand.Execute(vm.Years[0]);
        Assert.Equal(2023, vm.Year);
        Assert.True(vm.Years[0].IsActive);
        Assert.False(vm.Years[1].IsActive);

        vm.Update(days, DayOfWeek.Sunday);
        Assert.Equal(2023, vm.Year);

        vm.Update(Days((2024, 3, 14, 2)), DayOfWeek.Sunday);
        Assert.Equal(2024, vm.Year);
    }

    [Fact]
    public void TheSummary_CountsTheDays_AndNamesTheBusiest()
    {
        var vm = Calendar(Days((2024, 3, 14, 3.2), (2024, 3, 15, 1)));

        Assert.Equal("Listening on 2 of 366 days in 2024. The busiest was 2024-03-14, with 3h 12m.", vm.Summary);
    }

    [Fact]
    public void NoDatedPlays_SaysSo()
    {
        var vm = Calendar(new List<DateTimePoint>());

        Assert.False(vm.HasDays);
        Assert.Empty(vm.Weeks);
        Assert.Equal("No dated plays match the current filters.", vm.Summary);
    }

    [Fact]
    public void ADay_DescribesItselfForTooltipsAndScreenReaders()
    {
        Assert.Equal("Thursday 2024-03-14: 3h 12m", new CalendarDay(new DateTime(2024, 3, 14), 3.2, 4).Description);
        Assert.Equal("Friday 2024-03-15: nothing played", new CalendarDay(new DateTime(2024, 3, 15), 0, 0).Description);
    }

    [Fact]
    public void ADay_OpensItsOwnBreakdown()
    {
        Assert.Equal((DetailScope.Day, "2024-03-14", string.Empty),
            DetailWindow.TargetFor(new CalendarDay(new DateTime(2024, 3, 14), 1, 1)));
    }

    private static PlayRecord Play(string track, string artist, DateTime ts) => new()
    {
        TrackName = track,
        ArtistName = artist,
        AlbumName = "Album",
        MsPlayed = 200_000,
        Timestamp = ts,
    };

    [Fact]
    public void ADaysBreakdown_HoldsOnlyThatDaysPlays()
    {
        var records = new[]
        {
            Play("Morning", "A", new DateTime(2024, 3, 14, 8, 0, 0)),
            Play("Night", "B", new DateTime(2024, 3, 14, 23, 30, 0)),
            Play("Next Day", "A", new DateTime(2024, 3, 15, 0, 30, 0)),
            Play("Busiest Day", "C", new DateTime(2024, 3, 16, 9, 0, 0)),
            Play("Busiest Day", "C", new DateTime(2024, 3, 16, 10, 0, 0)),
            Play("Busiest Day", "C", new DateTime(2024, 3, 16, 11, 0, 0)),
        };
        var overall = AnalysisEngine.Analyze(records, new FilterOptions());

        var d = DetailEngine.Build(records, new FilterOptions(), DetailScope.Day, "2024-03-14", string.Empty, overall);

        Assert.Equal(2, d.PlayCount);
        Assert.Equal(new[] { "A", "B" }, d.Artists.Select(a => a.Artist).Order());
        Assert.Equal(new DateTime(2024, 3, 14, 8, 0, 0), d.FirstPlayed);
        Assert.Equal(2, d.Rank);
        Assert.Equal(3, d.RankOutOf);
        Assert.Equal(string.Empty, d.WebUrl);
    }

    [Fact]
    public void ADayThatIsNotADate_HasNoBreakdown()
    {
        var d = DetailEngine.Build(new[] { Play("A", "B", new DateTime(2024, 3, 14)) }, new FilterOptions(),
            DetailScope.Day, "not a day", string.Empty);

        Assert.Equal(0, d.PlayCount);
    }
}

/// <summary>The day breakdown's window.</summary>
[Collection(WpfCollection.Name)]
public class CalendarWindowTests
{
    [Fact]
    public void ADaysBreakdown_IsHeadedByItsWeekday_AndLeavesOutMonthsAndStreaks()
    {
        WpfTestHost.Run(() =>
        {
            var records = new[]
            {
                new PlayRecord { TrackName = "A", ArtistName = "X", AlbumName = "L", MsPlayed = 200_000, Timestamp = new DateTime(2024, 3, 14, 9, 0, 0) },
            };
            var d = DetailEngine.Build(records, new FilterOptions(), DetailScope.Day, "2024-03-14", string.Empty);
            var window = new DetailWindow(d);

            Assert.Equal("Thursday", DetailWindow.ScopeLabel(d));
            Assert.Equal(Visibility.Collapsed, window.MonthCard.Visibility);
            Assert.Equal(Visibility.Collapsed, window.BusiestCard.Visibility);
            Assert.Equal(Visibility.Collapsed, window.StreakCard.Visibility);
            Assert.Equal(Visibility.Visible, window.PlaysGrid.Columns[2].Visibility);
        });
    }
}
