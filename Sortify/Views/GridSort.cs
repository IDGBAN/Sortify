using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Sortify.Views;

/// <summary>
/// Puts the sort arrow on the column a grid's rows already arrive sorted by. The rows come
/// pre-sorted from the analysis (by listening time or by plays), so the grid itself has no
/// sort applied and would otherwise show no arrow until a header is clicked. Nothing is
/// re-sorted: only the column's arrow is set, and only while the user hasn't sorted by a
/// header themselves.
/// </summary>
public static class GridSort
{
    /// <summary>The SortMemberPath of the column the rows arrive sorted by.</summary>
    public static readonly DependencyProperty PresortedByProperty = DependencyProperty.RegisterAttached(
        "PresortedBy", typeof(string), typeof(GridSort), new PropertyMetadata(null, OnPresortedChanged));

    /// <summary>Which way the rows arrive sorted. Rankings are descending, the default.</summary>
    public static readonly DependencyProperty PresortedDirectionProperty = DependencyProperty.RegisterAttached(
        "PresortedDirection", typeof(ListSortDirection), typeof(GridSort),
        new PropertyMetadata(ListSortDirection.Descending, OnPresortedChanged));

    private static readonly DependencyProperty IsWatchingProperty = DependencyProperty.RegisterAttached(
        "IsWatching", typeof(bool), typeof(GridSort), new PropertyMetadata(false));

    public static string? GetPresortedBy(DataGrid grid) => (string?)grid.GetValue(PresortedByProperty);
    public static void SetPresortedBy(DataGrid grid, string? value) => grid.SetValue(PresortedByProperty, value);

    public static ListSortDirection GetPresortedDirection(DataGrid grid) =>
        (ListSortDirection)grid.GetValue(PresortedDirectionProperty);

    public static void SetPresortedDirection(DataGrid grid, ListSortDirection value) =>
        grid.SetValue(PresortedDirectionProperty, value);

    private static void OnPresortedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
            return;

        // The grid owns its item collection, so the handler lives and dies with the grid.
        if (!(bool)grid.GetValue(IsWatchingProperty))
        {
            grid.SetValue(IsWatchingProperty, true);
            ((INotifyCollectionChanged)grid.Items).CollectionChanged += (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Reset)
                    MarkPresortedColumn(grid);
            };
        }

        MarkPresortedColumn(grid);
    }

    /// <summary>
    /// Runs whenever the rows are replaced or re-sorted. Swapping the item source clears any
    /// sort the user picked, which puts the rows back in their own order, so the arrow goes
    /// back on the presorted column.
    /// </summary>
    internal static void MarkPresortedColumn(DataGrid grid)
    {
        var sorts = grid.Items.SortDescriptions;
        if (sorts.Count > 0)
        {
            // Sorted from a header. A plain click clears the other arrows itself, but a
            // Shift+click keeps them, which would leave the presorted arrow claiming an order
            // the rows are no longer in.
            foreach (var column in grid.Columns)
            {
                if (column.SortDirection is not null && !sorts.Any(s => s.PropertyName == column.SortMemberPath))
                    column.SortDirection = null;
            }
            return;
        }

        string? member = GetPresortedBy(grid);
        bool marked = false;
        foreach (var column in grid.Columns)
        {
            // Two columns can sort by the same member (Time and Hours); the first one gets it.
            bool match = !marked && !string.IsNullOrEmpty(member) && column.SortMemberPath == member;
            column.SortDirection = match ? GetPresortedDirection(grid) : null;
            marked |= match;
        }
    }
}
