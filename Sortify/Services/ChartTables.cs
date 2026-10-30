using System.Globalization;
using System.Text;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;
using Sortify.Models;
using LcDateTimePoint = LiveChartsCore.Defaults.DateTimePoint;

namespace Sortify.Services;

/// <summary>
/// A chart's numbers as rows and columns. Cells in a <see cref="Numeric"/> column are doubles,
/// or null where the chart has a gap; the rest are strings.
/// </summary>
public sealed class ChartTable
{
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<bool> Numeric { get; init; }
    public required IReadOnlyList<object?[]> Rows { get; init; }

    /// <summary>The table as CSV, with numbers written the same way whatever the system's culture.</summary>
    public string ToCsv()
    {
        var csv = new StringBuilder();
        csv.AppendJoin(',', Columns.Select(ExportService.Csv)).Append("\r\n");
        foreach (var row in Rows)
        {
            csv.AppendJoin(',', row.Select(cell => cell switch
            {
                double number => number.ToString("0.##", CultureInfo.InvariantCulture),
                string text => ExportService.Csv(text),
                _ => string.Empty,
            }));
            csv.Append("\r\n");
        }
        return csv.ToString();
    }
}

/// <summary>Reads the numbers back out of the charts <see cref="ChartBuilder"/> draws.</summary>
public static class ChartTables
{
    /// <summary>
    /// The table behind a chart, from its series and axes, or null when it has nothing to
    /// list. <paramref name="data"/>, when the chart has it, gives full names for the rows
    /// that the axis labels shorten.
    /// </summary>
    public static ChartTable? From(
        IEnumerable<ISeries>? series, IEnumerable<ICartesianAxis>? xAxes = null, IEnumerable<ICartesianAxis>? yAxes = null,
        ChartData? data = null)
    {
        var all = series?.Where(s => s.Values is not null).ToList() ?? new List<ISeries>();
        if (all.Count == 0)
            return null;

        var x = xAxes?.FirstOrDefault();
        var y = yAxes?.FirstOrDefault();
        var table = all.All(s => s.SeriesProperties.HasFlag(SeriesProperties.PieSeries)) ? Slices(all)
            : all[0].SeriesProperties.HasFlag(SeriesProperties.Heat) ? Heat(all[0], x, y)
            : Values(all[0]).FirstOrDefault(v => v is not null) is LcDateTimePoint ? Dated(all)
            : Categories(all, x, y, data);
        return table.Rows.Count == 0 ? null : table;
    }

    private static IEnumerable<object?> Values(ISeries series) => series.Values?.Cast<object?>() ?? Enumerable.Empty<object?>();

    private static double? Number(object? value) => value switch
    {
        double d => d,
        int i => i,
        LcDateTimePoint p => p.Value,
        WeightedPoint p => p.Weight,
        ObservableValue v => v.Value,
        _ => null,
    };

    // Each slice is a series of its own, holding one value.
    private static ChartTable Slices(List<ISeries> slices)
    {
        var first = slices.Select(s => s.Tag).OfType<ChartSlice>().FirstOrDefault();
        var rows = slices
            .Select(s => s.Tag is ChartSlice slice
                ? new object?[] { slice.Label, Number(Values(s).FirstOrDefault()), Math.Round(slice.SharePercent, 1) }
                : new object?[] { s.Name ?? string.Empty, Number(Values(s).FirstOrDefault()), null })
            .ToList();
        return new ChartTable
        {
            Columns = new[] { first?.Kind ?? "Name", first?.Unit ?? "Value", "Share %" },
            Numeric = new[] { false, true, true },
            Rows = rows,
        };
    }

    // One row per square: a day, an hour and its value, which sorts and copies better than a grid of 24 columns.
    private static ChartTable Heat(ISeries series, ICartesianAxis? x, ICartesianAxis? y)
    {
        var rows = Values(series)
            .OfType<WeightedPoint>()
            .Where(p => p.X is not null && p.Y is not null)
            .Select(p => new object?[] { Label(y, p.Y!.Value), Label(x, p.X!.Value), p.Weight })
            .ToList();
        return new ChartTable
        {
            Columns = new[] { y?.Name ?? "Day", x?.Name ?? "Hour", series.Name ?? "Value" },
            Numeric = new[] { false, false, true },
            Rows = rows,
        };
    }

    // Lines and columns over time: one row per date, a column for each series.
    private static ChartTable Dated(List<ISeries> all)
    {
        var byDate = new SortedDictionary<DateTime, double?[]>();
        for (int s = 0; s < all.Count; s++)
        {
            foreach (var point in Values(all[s]).OfType<LcDateTimePoint>())
            {
                if (!byDate.TryGetValue(point.DateTime, out var row))
                    byDate[point.DateTime] = row = new double?[all.Count];
                row[s] = point.Value;
            }
        }

        bool months = byDate.Keys.All(d => d.Day == 1 && d.TimeOfDay == TimeSpan.Zero);
        return new ChartTable
        {
            Columns = new[] { months ? "Month" : "Date" }.Concat(all.Select(SeriesName)).ToList(),
            Numeric = new[] { false }.Concat(all.Select(_ => true)).ToList(),
            Rows = byDate
                .Select(kv => new object?[] { months ? TimeFormat.Month(kv.Key) : TimeFormat.Day(kv.Key) }
                    .Concat(kv.Value.Select(v => (object?)v))
                    .ToArray())
                .ToList(),
        };
    }

    // Bars, columns, stacks and lines over named categories: one row per category, a column for each series.
    private static ChartTable Categories(List<ISeries> all, ICartesianAxis? x, ICartesianAxis? y, ChartData? data)
    {
        // A horizontal bar chart has its categories up the side, and draws the biggest at the
        // top by putting it last; the table puts it first.
        bool horizontal = all[0].SeriesProperties.HasFlag(SeriesProperties.PrimaryAxisHorizontalOrientation);
        var axis = horizontal ? y : x;
        var values = all.Select(s => Values(s).Select(Number).ToList()).ToList();
        int count = values.Max(v => v.Count);

        var items = data is { ItemsFollowSeries: false } && data.Items.Count == count ? data.Items : null;
        var names = items?.Select(Describe).ToList();
        bool hasNames = names is not null && names.All(n => n is not null);
        string kind = hasNames ? names![0]!.Kind : axis?.Name ?? "Label";
        string? secondKind = hasNames ? names![0]!.SecondKind : null;

        var rows = new List<object?[]>(count);
        for (int i = 0; i < count; i++)
        {
            var row = new List<object?> { hasNames ? names![i]!.Name : Label(axis, i) };
            if (secondKind is not null)
                row.Add(names![i]!.Second);
            row.AddRange(values.Select(v => i < v.Count ? (object?)v[i] : null));
            rows.Add(row.ToArray());
        }
        if (horizontal)
            rows.Reverse();

        var columns = new List<string> { kind };
        if (secondKind is not null)
            columns.Add(secondKind);
        columns.AddRange(all.Select(SeriesName));
        return new ChartTable
        {
            Columns = columns,
            Numeric = columns.Select((_, i) => i >= (secondKind is null ? 1 : 2)).ToList(),
            Rows = rows,
        };
    }

    private static string SeriesName(ISeries series, int index) => string.IsNullOrEmpty(series.Name) ? $"Series {index + 1}" : series.Name;

    private static string Label(ICartesianAxis? axis, double value)
    {
        int index = (int)Math.Round(value);
        if (axis?.Labels is { } labels && index >= 0 && index < labels.Count)
            return labels[index];
        return axis?.Labeler is { } labeler ? labeler(value) : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private sealed record RowName(string Name, string Kind, string? Second = null, string? SecondKind = null);

    /// <summary>A row's full name, where the chart's own label may be cut short.</summary>
    private static RowName? Describe(object item) => item switch
    {
        TrackStat t => new(t.Track, "Track", t.Artist, "Artist"),
        ArtistStat a => new(a.Artist, "Artist"),
        AlbumStat a => new(a.Album, "Album", a.Artist, "Artist"),
        ShowStat s => new(s.Show, "Show"),
        EpisodeStat e => new(e.Episode, "Episode", e.Show, "Show"),
        SkippedTrackStat s => new(s.Track, "Track", s.Artist, "Artist"),
        YearStat y => new(y.Year.ToString(CultureInfo.InvariantCulture), "Year"),
        _ => null,
    };
}
