using System.Text.Json;
using LiveChartsCore.SkiaSharpView;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>The day a week starts on.</summary>
public class WeekStartTests
{
    private static readonly DateTime Wednesday = new(2024, 3, 13, 18, 30, 0);

    [Theory]
    [InlineData(DayOfWeek.Sunday, 10)]
    [InlineData(DayOfWeek.Monday, 11)]
    [InlineData(DayOfWeek.Saturday, 9)]
    public void AWeek_StartsOnTheChosenDay(DayOfWeek first, int day)
    {
        Assert.Equal(new DateTime(2024, 3, day), Weeks.Start(Wednesday, first));
    }

    [Fact]
    public void TheFirstDayItself_StartsItsOwnWeek()
    {
        var monday = new DateTime(2024, 3, 11, 9, 0, 0);

        Assert.Equal(monday.Date, Weeks.Start(monday, DayOfWeek.Monday));
    }

    [Fact]
    public void TheDays_RunFromTheFirstOne()
    {
        Assert.Equal(
            new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday },
            Weeks.Ordered(DayOfWeek.Monday));
        Assert.Equal(DayOfWeek.Friday, Weeks.Ordered(DayOfWeek.Saturday)[^1]);
    }

    [Fact]
    public void TheArtistComparison_BucketsWeeksFromTheChosenDay()
    {
        var bucket = ArtistComparison.BucketOf(Wednesday, ChartBuilder.TimeGranularity.Weekly, DayOfWeek.Monday);

        Assert.Equal(new DateTime(2024, 3, 11), bucket);
    }

    [Fact]
    public void TheSidebar_ListsTheDaysFromTheChosenOne_WithoutReorderingWhatIsSaved()
    {
        var vm = new FilterViewModel();
        vm.SetFirstDayOfWeek(DayOfWeek.Monday);
        foreach (var day in vm.Days)
            day.IsSelected = day.Label is "Sat" or "Sun";

        Assert.Equal("Mon", vm.DaysInWeekOrder[0].Label);
        Assert.Contains("Sat, Sun", vm.Describe());

        // Saved sets keep Sunday first, so a set saved under one setting means the same under another.
        Assert.Equal(new[] { true, false, false, false, false, false, true }, vm.Snapshot().IncludedDays);
    }

    [Fact]
    public void Settings_StartOnSunday_AndKeepTheChoice()
    {
        Assert.Equal(DayOfWeek.Sunday, new AppSettings().FirstDayOfWeek);

        var json = JsonSerializer.Serialize(new AppSettings { FirstDayOfWeek = DayOfWeek.Monday });
        Assert.Contains("\"Monday\"", json);
        Assert.Equal(DayOfWeek.Monday, JsonSerializer.Deserialize<AppSettings>(json)!.FirstDayOfWeek);
    }

    [Fact]
    public void ADayNoCalendarStartsOn_FallsBackToSunday()
    {
        var settings = new AppSettings { FirstDayOfWeek = DayOfWeek.Wednesday };

        settings.Normalize();

        Assert.Equal(DayOfWeek.Sunday, settings.FirstDayOfWeek);
    }
}

/// <summary>The charts and the settings window following the first day.</summary>
[Collection(WpfCollection.Name)]
public class WeekStartChartTests
{
    private static AnalysisResult Result() => AnalysisEngine.Analyze(new[]
    {
        new PlayRecord { TrackName = "A", ArtistName = "X", AlbumName = "L", MsPlayed = 3_600_000, Timestamp = new DateTime(2024, 3, 11, 21, 0, 0) },
        new PlayRecord { TrackName = "B", ArtistName = "X", AlbumName = "L", MsPlayed = 7_200_000, Timestamp = new DateTime(2024, 3, 17, 9, 0, 0) },
    }, new FilterOptions());

    private static T OnUiThread<T>(Func<T> build)
    {
        T value = default!;
        WpfTestHost.Run(() => value = build());
        return value;
    }

    [Fact]
    public void TheDayOfWeekChart_StartsFromTheChosenDay()
    {
        var chart = OnUiThread(() => ChartBuilder.ByDayOfWeek(Result(), DayOfWeek.Monday));
        var axis = Assert.IsType<Axis>(chart.XAxes[0]);
        var values = (double[])((LiveChartsCore.SkiaSharpView.ColumnSeries<double>)chart.Series[0]).Values!;

        Assert.Equal("Mon", axis.Labels![0]);
        Assert.Equal("Sun", axis.Labels[6]);
        Assert.Equal(1, values[0]);
        Assert.Equal(2, values[6]);
    }

    [Fact]
    public void EveryHeatmapSquare_KnowsItsDayAndHour()
    {
        var chart = OnUiThread(() => ChartBuilder.DowHourHeat(Result(), DayOfWeek.Monday));

        Assert.Equal(7 * 24, chart.Items.Count);
        Assert.Equal(new ChartBuilder.HeatSlot(DayOfWeek.Monday, 0), chart.Items[0]);
        Assert.Equal(new ChartBuilder.HeatSlot(DayOfWeek.Monday, 21), chart.Items[21]);
        Assert.Equal(new ChartBuilder.HeatSlot(DayOfWeek.Sunday, 9), chart.Items[6 * 24 + 9]);
    }

    [Fact]
    public void WeeklyTotals_BeginOnTheChosenDay()
    {
        var chart = OnUiThread(() => ChartBuilder.OverTime(Result(), ChartBuilder.TimeGranularity.Weekly, DayOfWeek.Monday));
        var points = ((LiveChartsCore.SkiaSharpView.LineSeries<LiveChartsCore.Defaults.DateTimePoint>)chart.Series[0]).Values!.ToList();

        // Monday the 11th and Sunday the 17th fall in the same week when it starts on a Monday.
        var only = Assert.Single(points);
        Assert.Equal(new DateTime(2024, 3, 11), only.DateTime);
        Assert.Equal(3, only.Value);
    }

    [Fact]
    public void TheSettingsWindow_SavesTheChoice()
    {
        WpfTestHost.Run(() =>
        {
            var settings = new AppSettings();
            var window = new SettingsWindow(settings);
            Assert.Equal(0, window.WeekStartBox.SelectedIndex);

            window.WeekStartBox.SelectedIndex = 1;

            Assert.Equal(DayOfWeek.Monday, settings.FirstDayOfWeek);
            Assert.True(window.NeedsRefresh);
        });
    }
}
