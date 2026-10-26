using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Sortify.Models;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>The arrow on a grid's column header showing what the rows are sorted by.</summary>
[Collection(WpfCollection.Name)]
public class SortArrowTests
{
    private static readonly TrackStat[] Rows =
    {
        new() { Track = "Beta", Artist = "B", TotalMsPlayed = 3_000, PlayCount = 1 },
        new() { Track = "Alpha", Artist = "A", TotalMsPlayed = 1_000, PlayCount = 5 },
    };

    /// <summary>A grid laid out at a fixed size, with the app's styles, so its headers exist.</summary>
    private static DataGrid Grid()
    {
        var grid = new DataGrid();
        grid.Columns.Add(new DataGridTextColumn { Header = "Track", Binding = new Binding(nameof(TrackStat.Track)) });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Time",
            Binding = new Binding(nameof(TrackStat.TotalTime)),
            SortMemberPath = nameof(TrackStat.TotalMsPlayed),
        });
        grid.Columns.Add(new DataGridTextColumn { Header = "Plays", Binding = new Binding(nameof(TrackStat.PlayCount)) });
        return grid;
    }

    /// <summary>
    /// Lays the grid out at a fixed size. It sits in a window (never shown) first: an element
    /// outside any tree picks up neither the app's implicit style nor its theme template.
    /// </summary>
    private static void Layout(DataGrid grid)
    {
        _ = new Window { Content = grid };
        grid.Measure(new Size(600, 300));
        grid.Arrange(new Rect(0, 0, 600, 300));
        grid.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }

    private static DataGridColumnHeader Header(DataGrid grid, string name) =>
        Descendants(grid).OfType<DataGridColumnHeader>().First(h => Equals(h.Column?.Header, name));

    /// <summary>The arrow the header template draws, or null while it is hidden.</summary>
    private static string? Arrow(DataGrid grid, string column)
    {
        var arrow = Descendants(Header(grid, column)).OfType<TextBlock>().Single(t => t.Name == "SortArrow");
        return arrow.Visibility == Visibility.Visible ? arrow.Text : null;
    }

    /// <summary>What a mouse click on a header runs: ButtonBase.OnClick, which the header overrides to sort.</summary>
    private static void Click(DataGrid grid, string column)
    {
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(Header(grid, column), null);
        grid.UpdateLayout();
    }

    private const string Up = "\uE74A";
    private const string Down = "\uE74B";

    [Fact]
    public void ClickingAHeader_ShowsWhichWayItSorts()
    {
        WpfTestHost.Run(() =>
        {
            var grid = Grid();
            grid.ItemsSource = Rows;
            Layout(grid);
            Assert.Null(Arrow(grid, "Track"));

            Click(grid, "Track");
            Assert.Equal(Up, Arrow(grid, "Track"));
            Assert.Equal("Alpha", ((TrackStat)grid.Items[0]).Track);

            Click(grid, "Track");
            Assert.Equal(Down, Arrow(grid, "Track"));
            Assert.Equal("Beta", ((TrackStat)grid.Items[0]).Track);

            // Sorting by another column moves the arrow there.
            Click(grid, "Plays");
            Assert.Null(Arrow(grid, "Track"));
            Assert.Equal(Up, Arrow(grid, "Plays"));
        });
    }

    [Fact]
    public void APresortedGrid_ShowsItsOrderUntilAHeaderIsClicked()
    {
        WpfTestHost.Run(() =>
        {
            var grid = Grid();
            GridSort.SetPresortedBy(grid, nameof(TrackStat.PlayCount));
            grid.ItemsSource = Rows.OrderByDescending(t => t.PlayCount).ToList();
            Layout(grid);

            Assert.Equal(ListSortDirection.Descending, grid.Columns[2].SortDirection);
            Assert.Equal(Down, Arrow(grid, "Plays"));
            Assert.Empty(grid.Items.SortDescriptions);

            // The user's own sort replaces it, and clicking the presorted column reverses it.
            Click(grid, "Track");
            Assert.Null(grid.Columns[2].SortDirection);
            Assert.Equal(Up, Arrow(grid, "Track"));

            // New rows (a re-analysis, or the Time / Plays switch) come back in their own order.
            grid.ItemsSource = Rows.OrderByDescending(t => t.TotalMsPlayed).ToList();
            GridSort.SetPresortedBy(grid, nameof(TrackStat.TotalMsPlayed));
            grid.UpdateLayout();
            Assert.Equal(new ListSortDirection?[] { null, ListSortDirection.Descending, null },
                grid.Columns.Select(c => c.SortDirection));

            Click(grid, "Time");
            Assert.Equal(Up, Arrow(grid, "Time"));
            Assert.Equal("Alpha", ((TrackStat)grid.Items[0]).Track);
        });
    }

    [Fact]
    public void APresortedDirection_CanBeAscending()
    {
        WpfTestHost.Run(() =>
        {
            var grid = Grid();
            GridSort.SetPresortedDirection(grid, ListSortDirection.Ascending);
            GridSort.SetPresortedBy(grid, nameof(TrackStat.Track));
            grid.ItemsSource = Rows.OrderBy(t => t.Track).ToList();

            Assert.Equal(ListSortDirection.Ascending, grid.Columns[0].SortDirection);
        });
    }
}
