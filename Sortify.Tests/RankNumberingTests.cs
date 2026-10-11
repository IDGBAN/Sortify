using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using LiveChartsCore.SkiaSharpView;
using Sortify.Models;
using Sortify.Services;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>Top lists, bar charts and grids number their entries, apart from the names.</summary>
[Collection(WpfCollection.Name)]
public class RankNumberingTests
{
    private static AnalysisResult Result()
    {
        PlayRecord Play(string track, int minutes) => new()
        {
            TrackName = track,
            ArtistName = track + " Band",
            AlbumName = track + " LP",
            MsPlayed = minutes * 60_000,
            Timestamp = new DateTime(2024, 3, 1, 12, 0, 0),
        };

        return AnalysisEngine.Analyze(new[] { Play("Big", 90), Play("Middle", 60), Play("Small", 30) },
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
    public void BarCharts_RankEachBarOnItsOwnAxis()
    {
        var charts = OnUiThread(() => new[]
        {
            ChartBuilder.TopTracksByTime(Result(), 3),
            ChartBuilder.TopTracksByCount(Result(), 3),
            ChartBuilder.TopArtistsByTime(Result(), 3),
            ChartBuilder.TopArtistsByCount(Result(), 3),
            ChartBuilder.TopAlbumsByTime(Result(), 3),
            ChartBuilder.TopAlbumsByCount(Result(), 3),
        });

        foreach (var chart in charts)
        {
            Assert.Equal(2, chart.YAxes.Length);
            var ranks = chart.YAxes[0].Labels!;
            var names = chart.YAxes[1].Labels!;

            // Bars run bottom-up, so the top bar, ranked 1, is last; names stay bare.
            Assert.Equal(new[] { "3", "2", "1" }, ranks);
            Assert.Equal(new[] { "Small", "Middle", "Big" }, names.Select(n => n.Split(' ')[0]));
            Assert.DoesNotContain(names, n => char.IsDigit(n[0]));

            // The bars scale against the names, and the rank axis spans the same rows.
            Assert.Equal(1, ((RowSeries<double>)chart.Series[0]).ScalesYAt);
            Assert.Equal(chart.YAxes[1].MinLimit, chart.YAxes[0].MinLimit);
            Assert.Equal(chart.YAxes[1].MaxLimit, chart.YAxes[0].MaxLimit);
            Assert.Equal(-0.5, chart.YAxes[0].MinLimit);
            Assert.Equal(2.5, chart.YAxes[0].MaxLimit);
        }
    }

    [Fact]
    public void AnEmptyBarChart_HasNoRanks()
    {
        var chart = OnUiThread(() => ChartBuilder.TopTracksByTime(AnalysisResult.Empty, 25));

        Assert.Empty(chart.YAxes[0].Labels!);
    }

    [Fact]
    public void RankLabels_CountDownToOne()
    {
        Assert.Equal(new[] { "4", "3", "2", "1" }, ChartBuilder.RankLabels(4));
        Assert.Empty(ChartBuilder.RankLabels(0));
    }

    [Fact]
    public void InsightDonuts_RankTheirSlicesButNotOther()
    {
        var r = new AnalysisResult
        {
            ReasonEnds = Enumerable.Range(0, 10)
                .Select(i => new ReasonEndStat { Reason = i == 0 ? "trackdone" : "r" + i, Count = 20 - i })
                .ToArray(),
            Platforms = new[] { new ContextStat { Name = "Phone", TotalMsPlayed = 7_200_000 }, new ContextStat { Name = "PC", TotalMsPlayed = 3_600_000 } },
            Countries = Array.Empty<ContextStat>(),
        };

        var (reasons, platforms, countries) = OnUiThread(() => (
            ChartBuilder.ReasonEndSlicesOf(r), ChartBuilder.PlatformSlicesOf(r), ChartBuilder.CountrySlicesOf(r)));

        // Eight named reasons, ranked, then the last two folded into an unranked "Other".
        Assert.Equal(Enumerable.Range(1, 8).Select(i => (int?)i).Append(null), reasons.Select(s => s.Rank));
        Assert.Equal("Track finished", reasons[0].Name);
        Assert.Equal("Other", reasons[^1].Name);
        Assert.Equal(new int?[] { 1, 2 }, platforms.Select(s => s.Rank));
        Assert.Equal(new[] { "Phone", "PC" }, platforms.Select(s => s.Name));
        Assert.Empty(countries);

        // The donut itself keeps one slice per legend row, in the same order.
        var series = OnUiThread(() => ChartBuilder.ReasonEndShare(r));
        Assert.Equal(reasons.Count, series.Length);
        Assert.Equal("Track finished", series[0].Name);
    }

    [Fact]
    public void ArtistShare_RanksTheTopArtistsButNotOther()
    {
        var withOther = new AnalysisResult
        {
            Artists = new[]
            {
                new ArtistStat { Artist = "A", TotalMsPlayed = 3_600_000 },
                new ArtistStat { Artist = "B", TotalMsPlayed = 1_800_000 },
            },
            TotalMsPlayed = 7_200_000,
        };

        var slices = OnUiThread(() => ChartBuilder.ArtistShareSlicesOf(withOther));

        Assert.Equal(new int?[] { 1, 2, null }, slices.Select(s => s.Rank));
        Assert.Equal(new[] { "A", "B", "Other" }, slices.Select(s => s.Name));
        Assert.Equal(50, slices[0].Percent, 3);
    }

    [Fact]
    public void ArtistShareLegend_KeepsTheRankOutOfTheName()
    {
        var entries = OnUiThread(() => ChartBuilder.ArtistShareSlicesOf(Result())
            .Select(ViewModels.LegendEntry.From)
            .ToList());

        Assert.Equal(new[] { "1", "2", "3" }, entries.Select(e => e.Rank));
        Assert.Equal(new[] { "Big Band", "Middle Band", "Small Band" }, entries.Select(e => e.Name));
        Assert.Equal("50%", entries[0].Share);
    }

    private sealed record Row(string Name, int Value);

    private static List<DataGridRow> Rows(DependencyObject parent)
    {
        var rows = new List<DataGridRow>();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is DataGridRow row)
                rows.Add(row);
            else
                rows.AddRange(Rows(child));
        }
        return rows.OrderBy(r => r.GetIndex()).ToList();
    }

    // Rows are only generated once the grid is on screen, so it goes in a window that is
    // never shown (no WS_VISIBLE).
    private static HwndSource Host(FrameworkElement element) =>
        new(new HwndSourceParameters("RankNumberingTests") { Width = 400, Height = 400, WindowStyle = 0 })
        {
            RootVisual = element,
        };

    private static void Layout(FrameworkElement element) => element.UpdateLayout();

    [Fact]
    public void Grids_NumberRowsInTheOrderShown()
    {
        WpfTestHost.Run(() =>
        {
            var grid = new DataGrid();
            RowNumbers.SetIsEnabled(grid, true);
            grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new System.Windows.Data.Binding("Name") });
            grid.ItemsSource = new[] { new Row("b", 2), new Row("a", 3), new Row("c", 1) };
            using var host = Host(grid);
            Layout(grid);

            var number = grid.Columns[0];
            Assert.Equal(RowNumbers.Header, number.Header);
            Assert.False(number.CanUserSort);
            Assert.Equal("Name", grid.Columns[1].Header);
            Assert.Equal(new[] { 1, 2, 3 }, Rows(grid).Select(RowNumbers.GetNumber));

            // Sorted another way, the numbers still count down the rows as they now stand.
            grid.Items.SortDescriptions.Add(new SortDescription("Value", ListSortDirection.Ascending));
            Layout(grid);
            var rows = Rows(grid);
            Assert.Equal(new[] { "c", "b", "a" }, rows.Select(r => ((Row)r.Item).Name));
            Assert.Equal(new[] { 1, 2, 3 }, rows.Select(RowNumbers.GetNumber));

            // And a filter that drops a row closes the gap.
            grid.Items.Filter = o => ((Row)o).Name != "c";
            Layout(grid);
            Assert.Equal(new[] { 1, 2 }, Rows(grid).Select(RowNumbers.GetNumber));
        });
    }

    [Fact]
    public void EveryRankedGridHasANumberColumn()
    {
        WpfTestHost.Run(() =>
        {
            var window = new MainWindow();
            foreach (var name in new[] { "TracksGrid", "ArtistsGrid", "AlbumsGrid", "YearsGrid", "ShowsGrid", "EpisodesGrid" })
            {
                var grid = (DataGrid)window.FindName(name);
                Assert.Equal(RowNumbers.Header, grid.Columns[0].Header);
                Assert.Equal(1, grid.Columns.Count(c => Equals(c.Header, RowNumbers.Header)));
            }
        });
    }
}
