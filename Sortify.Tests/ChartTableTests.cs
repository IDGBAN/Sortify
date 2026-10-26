using System.Data;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WPF;
using Sortify.Models;
using Sortify.Services;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>The numbers behind each kind of chart, read back out as a table.</summary>
[Collection(WpfCollection.Name)]
public class ChartTableTests
{
    private const string LongTrack = "A track title far too long for a bar's label";

    private static PlayRecord Play(string track, string artist, int minutes, DateTime when) => new()
    {
        TrackName = track,
        ArtistName = artist,
        AlbumName = artist + " LP",
        MsPlayed = minutes * 60_000,
        Timestamp = when,
    };

    private static readonly AnalysisResult Result = AnalysisEngine.Analyze(new[]
    {
        Play(LongTrack, "Band", 120, new DateTime(2023, 3, 1, 9, 0, 0)),
        Play("Short", "Other", 30, new DateTime(2024, 3, 1, 21, 0, 0)),
        Play("Short", "Other", 30, new DateTime(2024, 3, 2, 21, 0, 0)),
    }, new FilterOptions());

    // The chart builder reaches the palette, whose brushes come from the WPF resources.
    private static T OnUiThread<T>(Func<T> build)
    {
        T value = default!;
        WpfTestHost.Run(() => value = build());
        return value;
    }

    private static ChartTable Table(ChartData chart) => ChartTables.From(chart.Series, chart.XAxes, chart.YAxes, chart)!;

    [Fact]
    public void BarCharts_ListTheBiggestFirst_UnderTheirFullNames()
    {
        var table = Table(OnUiThread(() => ChartBuilder.TopTracksByTime(Result, 5)));

        Assert.Equal(new[] { "Track", "Artist", "Hours" }, table.Columns);
        Assert.Equal(new[] { false, false, true }, table.Numeric);
        Assert.Equal(new object?[] { LongTrack, "Band", 2.0 }, table.Rows[0]);
        Assert.Equal(new object?[] { "Short", "Other", 1.0 }, table.Rows[1]);
    }

    [Fact]
    public void ColumnCharts_UseTheirAxisLabels()
    {
        var table = Table(OnUiThread(() => ChartBuilder.ByHour(Result)));

        Assert.Equal(new[] { "Hour of day", "Hours" }, table.Columns);
        Assert.Equal(24, table.Rows.Count);
        Assert.Equal(new object?[] { "09", 2.0 }, table.Rows[9]);
    }

    [Fact]
    public void TheYearsChart_NamesEachYear()
    {
        var table = Table(OnUiThread(() => ChartBuilder.PerYear(Result)));

        Assert.Equal("Year", table.Columns[0]);
        Assert.Equal(new[] { "2023", "2024" }, table.Rows.Select(r => r[0]));
    }

    [Fact]
    public void StackedColumns_GiveEachBandAColumn()
    {
        var records = new[] { Play("Old", "Band", 60, new DateTime(2023, 3, 1)), Play("Old", "Band", 60, new DateTime(2024, 3, 1)) };
        var cohorts = DiscoveryCohorts.Build(records, new FilterOptions());
        var years = AnalysisEngine.Analyze(records, new FilterOptions()).Years;

        var table = Table(OnUiThread(() => ChartBuilder.Cohorts(cohorts, years)));

        Assert.Equal("Year", table.Columns[0]);
        Assert.Equal("Found that year", table.Columns[1]);
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public void TheRankHistory_HasAYearPerRow_WithAGapWhereAnArtistWasOutside()
    {
        var result = RankHistoryTests.ThreeArtists();

        var table = Table(OnUiThread(() => ChartBuilder.RankHistory(result)));

        Assert.Equal(new[] { "Year", "Riser", "Steady", "Faded" }, table.Columns);
        Assert.Equal(new object?[] { "2023", 3.0, 2.0, 1.0 }, table.Rows[0]);
        Assert.Equal(new object?[] { "2024", 1.0, 2.0, null }, table.Rows[1]);
        Assert.EndsWith("2024,1,2,\r\n", table.ToCsv());
    }

    [Fact]
    public void TimeCharts_ListEachDayOrMonth()
    {
        var daily = Table(OnUiThread(() => ChartBuilder.OverTime(Result)));
        Assert.Equal(new[] { "Date", "Hours/day" }, daily.Columns);
        Assert.Equal(new object?[] { "2023-03-01", 2.0 }, daily.Rows[0]);

        var monthly = Table(OnUiThread(() => ChartBuilder.OverTime(Result, ChartBuilder.TimeGranularity.Monthly)));
        Assert.Equal(new[] { "Month", "Hours/month" }, monthly.Columns);
        Assert.Equal(new object?[] { "2024-03", 1.0 }, monthly.Rows[^1]);
    }

    [Fact]
    public void TimeCharts_ShowAQuietStretchAsZeros_RatherThanSkippingIt()
    {
        var daily = Table(OnUiThread(() => ChartBuilder.OverTime(Result)));
        Assert.Equal(new object?[] { "2023-03-02", 0.0 }, daily.Rows[1]);
        Assert.Equal(new object?[] { "2024-03-02", 0.5 }, daily.Rows[^1]);
        Assert.Equal(368, daily.Rows.Count);

        var weekly = Table(OnUiThread(() => ChartBuilder.OverTime(Result, ChartBuilder.TimeGranularity.Weekly)));
        Assert.Equal(new object?[] { "2023-03-05", 0.0 }, weekly.Rows[1]);

        var monthly = Table(OnUiThread(() => ChartBuilder.OverTime(Result, ChartBuilder.TimeGranularity.Monthly)));
        Assert.Equal(13, monthly.Rows.Count);
        Assert.Equal(new object?[] { "2023-04", 0.0 }, monthly.Rows[1]);
    }

    [Fact]
    public void TheHeatmap_ListsEveryHourOfEveryDay()
    {
        var table = Table(OnUiThread(() => ChartBuilder.DowHourHeat(Result)));

        Assert.Equal(new[] { "Day", "Hour", "Hours" }, table.Columns);
        Assert.Equal(7 * 24, table.Rows.Count);
        Assert.Contains(table.Rows, r => Equals(r[0], "Wed") && Equals(r[1], "09") && Equals(r[2], 2.0));
    }

    [Fact]
    public void Pies_ListEachSliceWithItsShare()
    {
        var table = ChartTables.From(OnUiThread(() => ChartBuilder.ArtistShare(Result)))!;

        Assert.Equal(new[] { "Artist", "Hours", "Share %" }, table.Columns);
        Assert.Equal(new object?[] { "Band", 2.0, 66.7 }, table.Rows[0]);
        Assert.Equal(new object?[] { "Other", 1.0, 33.3 }, table.Rows[1]);
    }

    [Fact]
    public void AChartWithNothingInIt_HasNoTable()
    {
        Assert.Null(ChartTables.From(Array.Empty<ISeries>()));
        Assert.Null(ChartTables.From(null));
        Assert.Null(ChartTables.From(new ISeries[] { new ColumnSeries<double> { Values = Array.Empty<double>() } }));
    }

    [Fact]
    public void TheCsv_QuotesWhatNeedsIt_AndWritesNumbersTheSameEverywhere()
    {
        using var _ = CultureScope.Named("de-DE");
        var table = new ChartTable
        {
            Columns = new[] { "Name", "Hours" },
            Numeric = new[] { false, true },
            Rows = new List<object?[]> { new object?[] { "Crosby, Stills & Nash", 1.5 }, new object?[] { "Say \"hi\"", null } },
        };

        Assert.Equal("Name,Hours\r\n\"Crosby, Stills & Nash\",1.5\r\n\"Say \"\"hi\"\"\",\r\n", table.ToCsv());
    }

    [Fact]
    public void TheWindow_ShowsEveryRow_AndSortsNumbersAsNumbers()
    {
        WpfTestHost.Run(() =>
        {
            var table = new ChartTable
            {
                Columns = new[] { "Hour", "Hours" },
                Numeric = new[] { false, true },
                Rows = new List<object?[]> { new object?[] { "a", 9.0 }, new object?[] { "b", 10.0 }, new object?[] { "c", null } },
            };
            var window = new ChartTableWindow("By Hour", table);
            var grid = (DataGrid)window.FindName("Table");

            Assert.Equal("By Hour", window.Title);
            Assert.Equal(new[] { "Hour", "Hours" }, grid.Columns.Select(c => (string)c.Header));
            var view = (DataView)grid.ItemsSource;
            Assert.Equal(3, view.Count);

            // As text, "10" would sort before "9".
            view.Sort = grid.Columns[1].SortMemberPath + " DESC";
            Assert.Equal(10.0, view[0][1]);
        });
    }

    [Fact]
    public void EveryChartInTheMainWindow_CanBeReachedByKeyboard_AndIsNamedAfterItsCard()
    {
        WpfTestHost.Run(() =>
        {
            var window = new MainWindow();
            var charts = Descendants(window).OfType<Control>().Where(c => c is CartesianChart or PieChart).ToList();

            Assert.True(charts.Count >= 20);
            Assert.All(charts, c =>
            {
                Assert.True(c.Focusable);
                Assert.NotEqual("Chart", AutomationProperties.GetName(c));
            });
            Assert.Contains(charts, c => AutomationProperties.GetName(c) == "Top Tracks");
            Assert.Contains(charts, c => AutomationProperties.GetName(c) == "Listening Heatmap (day of week vs. hour of day)");

            var menu = (ContextMenu)window.FindResource("ChartMenu");
            Assert.Contains(menu.Items.OfType<MenuItem>(), i => (string)i.Header == "Show the numbers...");
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
