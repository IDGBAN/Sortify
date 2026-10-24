using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace Sortify.Views;

/// <summary>
/// Numbers a DataGrid's rows in a "#" column of its own, ahead of the rest: set
/// <c>views:RowNumbers.IsEnabled="True"</c> on the grid. The number is the row's place as
/// shown, so it follows the grid's sort order and any row filter, and is kept apart from
/// the row's data rather than written into a name.
/// </summary>
public static class RowNumbers
{
    public const string Header = "#";

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(RowNumbers), new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>A row's 1-based place in its grid, which the "#" column shows.</summary>
    public static readonly DependencyProperty NumberProperty = DependencyProperty.RegisterAttached(
        "Number", typeof(int), typeof(RowNumbers), new PropertyMetadata(0));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    public static int GetNumber(DependencyObject d) => (int)d.GetValue(NumberProperty);

    public static void SetNumber(DependencyObject d, int value) => d.SetValue(NumberProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid || e.NewValue is not true)
            return;

        // Set from XAML before the grid's own columns are added, so this lands first.
        grid.Columns.Insert(0, NumberColumn());
        grid.LoadingRow += (_, args) => SetNumber(args.Row, args.Row.GetIndex() + 1);

        // Sorting and filtering regenerate the rows, which numbers them afresh through
        // LoadingRow. A row added or removed in place shifts the ones after it without
        // reloading them, so those are renumbered once the grid has caught up.
        ((INotifyCollectionChanged)grid.Items).CollectionChanged += (_, _) =>
            grid.Dispatcher.BeginInvoke(() => Renumber(grid), DispatcherPriority.Loaded);
    }

    private static DataGridTextColumn NumberColumn() => new()
    {
        Header = Header,
        Width = DataGridLength.Auto,
        CanUserSort = false,
        CanUserReorder = false,
        Binding = new Binding
        {
            Path = new PropertyPath(NumberProperty),
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1),
        },
        ElementStyle = Application.Current?.TryFindResource("RowNumberText") as Style,
    };

    private static void Renumber(DataGrid grid)
    {
        foreach (var row in Rows(grid))
        {
            int index = row.GetIndex();
            if (index >= 0)
                SetNumber(row, index + 1);
        }
    }

    private static IEnumerable<DataGridRow> Rows(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is DataGridRow row)
            {
                yield return row;
                continue;
            }
            foreach (var nested in Rows(child))
                yield return nested;
        }
    }
}
