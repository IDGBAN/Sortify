using System.Data;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using LiveChartsCore.SkiaSharpView.WPF;
using Sortify.Services;

namespace Sortify.Views;

/// <summary>
/// The numbers behind a chart as a sortable table: for reading exact figures, copying them
/// out, or, with a screen reader, getting at a chart at all.
/// </summary>
public partial class ChartTableWindow : ThemedWindow
{
    // Column widths: names get room but not the whole window, numbers never squeeze below a few digits.
    private const double TextColumnMinWidth = 90;
    private const double TextColumnMaxWidth = 360;
    private const double NumberColumnMinWidth = 70;

    private readonly ChartTable _table;

    public ChartTableWindow(string title, ChartTable table)
    {
        InitializeComponent();
        _table = table;
        Title = title;
        HeaderText.Text = title;
        CountText.Text = $"{Plural.Of(table.Rows.Count, "row")}. Click a column's header to sort by it.";
        AutomationProperties.SetName(Table, title);

        // Bound through a DataTable so every column sorts by its own type, numbers as numbers.
        var data = new DataTable();
        for (int i = 0; i < table.Columns.Count; i++)
            data.Columns.Add($"c{i}", table.Numeric[i] ? typeof(double) : typeof(string));
        foreach (var row in table.Rows)
            data.Rows.Add(row.Select(cell => cell ?? DBNull.Value).ToArray());

        // Every column sizes to what it holds. A star column beside auto ones overflows once
        // the rows come in and the auto columns widen, pushing the last column out of view.
        var trimmed = new Style(typeof(TextBlock), DataGridTextColumn.DefaultElementStyle);
        trimmed.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        for (int i = 0; i < table.Columns.Count; i++)
        {
            Table.Columns.Add(new DataGridTextColumn
            {
                Header = table.Columns[i],
                Binding = new Binding($"[c{i}]") { StringFormat = table.Numeric[i] ? "#,0.##" : null },
                SortMemberPath = $"c{i}",
                Width = DataGridLength.Auto,
                MinWidth = table.Numeric[i] ? NumberColumnMinWidth : TextColumnMinWidth,
                MaxWidth = table.Numeric[i] ? double.PositiveInfinity : TextColumnMaxWidth,
                ElementStyle = trimmed,
            });
        }
        Table.ItemsSource = data.DefaultView;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_table.ToCsv());
            ResultText.Text = $"Copied {Plural.Of(_table.Rows.Count, "row")} as CSV.";
        }
        catch (ExternalException ex)
        {
            // The clipboard can be locked by another process.
            ResultText.Text = $"Could not copy the table: {ex.Message}";
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>Opens the numbers behind a chart, or behind the chart on show in a card. False when there are none.</summary>
    public static bool ShowFor(FrameworkElement element, Window owner)
    {
        if (TableFor(element) is not { } table)
            return false;

        new ChartTableWindow(TitleFor(element), table) { Owner = owner }.ShowDialog();
        return true;
    }

    /// <summary>The numbers behind a chart, or behind the chart on show in a card. Null when there are none.</summary>
    public static ChartTable? TableFor(DependencyObject element) => (element is CartesianChart or PieChart ? element : VisibleChartIn(element)) switch
    {
        CartesianChart chart => ChartTables.From(chart.Series, chart.XAxes, chart.YAxes, chart.Tag as ChartData),
        PieChart pie => ChartTables.From(pie.Series),
        _ => null,
    };

    // A card can hold two charts and show one of them, like the Tracks card's time and plays.
    private static DependencyObject? VisibleChartIn(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is CartesianChart { IsVisible: true } or PieChart { IsVisible: true })
                return child;
            if (VisibleChartIn(child) is { } chart)
                return chart;
        }
        return null;
    }

    /// <summary>The heading of the card a chart sits in, such as "Top Tracks".</summary>
    public static string TitleFor(FrameworkElement element)
    {
        object? header = element.TryFindResource("SectionHeader");
        for (DependencyObject? at = element; at is not null and not TabItem and not Window;
             at = LogicalTreeHelper.GetParent(at) ?? (at is Visual visual ? VisualTreeHelper.GetParent(visual) : null))
        {
            if (HeaderIn(at, header) is { Length: > 0 } title)
                return title;
        }
        return "Chart";
    }

    // A heading built from runs carries a hint after the title; the first run is the title.
    private static string? HeaderIn(DependencyObject root, object? header)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is TextBlock text && header is not null && ReferenceEquals(text.Style, header))
                return text.Inlines.FirstInline is Run run ? run.Text.Trim() : text.Text.Trim();
            if (HeaderIn(child, header) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Lets the keyboard reach every chart in <paramref name="window"/>: each takes focus, is
    /// named after its card, and opens its numbers on Enter. <paramref name="nothingToShow"/>
    /// is told when a chart has none.
    /// </summary>
    public static void MakeChartsReachable(Window window, Action? nothingToShow = null)
    {
        var ring = window.TryFindResource("FocusRing") as Style;
        foreach (var chart in LogicalDescendants(window).OfType<Control>().Where(c => c is CartesianChart or PieChart).ToList())
        {
            chart.Focusable = true;
            chart.IsTabStop = true;
            chart.FocusVisualStyle = ring;
            // A chart whose heading is bound, like the Years chart's, binds its name to the same text.
            if (!BindingOperations.IsDataBound(chart, AutomationProperties.NameProperty))
                AutomationProperties.SetName(chart, TitleFor(chart));
            AutomationProperties.SetHelpText(chart, "Press Enter for this chart's numbers as a table, or Shift+F10 for its menu.");
            chart.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter)
                    return;
                e.Handled = true;
                if (!ShowFor(chart, window))
                    nothingToShow?.Invoke();
            };
        }
    }

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in LogicalDescendants(child))
                yield return descendant;
        }
    }
}
