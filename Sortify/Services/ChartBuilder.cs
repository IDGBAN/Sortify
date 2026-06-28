using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Sortify.Models;
using LcDateTimePoint = LiveChartsCore.Defaults.DateTimePoint;

namespace Sortify.Services;

/// <summary>Builds LiveCharts2 series and axes from an <see cref="AnalysisResult"/>.</summary>
public static class ChartBuilder
{
    public const int TopN = 15;

    private static readonly SKColor Accent = new(29, 185, 84);   // Spotify green
    private static readonly SKColor Accent2 = new(80, 156, 248);
    private static readonly SKColor Text = new(220, 220, 220);

    private static readonly SKColor[] Palette =
    {
        new(29, 185, 84), new(80, 156, 248), new(244, 162, 97), new(231, 111, 81),
        new(42, 157, 143), new(233, 196, 106), new(155, 93, 229), new(247, 37, 133),
        new(76, 201, 240), new(181, 23, 158), new(114, 9, 183), new(58, 134, 255),
        new(255, 159, 28), new(46, 196, 182), new(255, 89, 94),
    };

    private static SolidColorPaint Label() => new(Text);

    private static string ShortLabel(string s, int max = 22)
        => s.Length <= max ? s : s[..(max - 1)] + "\u2026";

    // ---- Horizontal bar charts (RowSeries) -------------------------------------------------

    public static (ISeries[] series, Axis[] x, Axis[] y) TopTracksByTime(AnalysisResult r)
    {
        var items = r.Tracks.Take(TopN).Reverse().ToList();
        var values = items.Select(t => Math.Round(t.TotalHours, 2)).ToArray();
        var labels = items.Select(t => ShortLabel($"{t.Track} - {t.Artist}")).ToArray();
        return Rows(values, labels, "Hours", Accent);
    }

    public static (ISeries[] series, Axis[] x, Axis[] y) TopTracksByCount(AnalysisResult r)
    {
        var items = r.Tracks.OrderByDescending(t => t.PlayCount).Take(TopN).Reverse().ToList();
        var values = items.Select(t => (double)t.PlayCount).ToArray();
        var labels = items.Select(t => ShortLabel($"{t.Track} - {t.Artist}")).ToArray();
        return Rows(values, labels, "Plays", Accent2);
    }

    public static (ISeries[] series, Axis[] x, Axis[] y) TopArtistsByTime(AnalysisResult r)
    {
        var items = r.Artists.Take(TopN).Reverse().ToList();
        var values = items.Select(a => Math.Round(a.TotalHours, 2)).ToArray();
        var labels = items.Select(a => ShortLabel(a.Artist)).ToArray();
        return Rows(values, labels, "Hours", Accent);
    }

    public static (ISeries[] series, Axis[] x, Axis[] y) TopArtistsByCount(AnalysisResult r)
    {
        var items = r.Artists.OrderByDescending(a => a.PlayCount).Take(TopN).Reverse().ToList();
        var values = items.Select(a => (double)a.PlayCount).ToArray();
        var labels = items.Select(a => ShortLabel(a.Artist)).ToArray();
        return Rows(values, labels, "Plays", Accent2);
    }

    private static (ISeries[], Axis[], Axis[]) Rows(double[] values, string[] labels, string unit, SKColor color)
    {
        var series = new ISeries[]
        {
            new RowSeries<double>
            {
                Values = values,
                Name = unit,
                Fill = new SolidColorPaint(color),
                DataLabelsPaint = new SolidColorPaint(Text),
                DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Right,
                DataLabelsFormatter = p => p.Coordinate.PrimaryValue.ToString("0.##"),
                DataLabelsSize = 11,
                Padding = 2,
            },
        };
        var y = new[]
        {
            // One label per bar, smaller text so the 15 category names don't overlap.
            new Axis
            {
                Labels = labels,
                LabelsPaint = Label(),
                TextSize = 11,
                MinStep = 1,
                ForceStepToMin = true,
                SeparatorsPaint = null,
            },
        };
        var x = new[]
        {
            new Axis { Name = unit, NamePaint = Label(), LabelsPaint = Label(), TextSize = 11, MinLimit = 0 },
        };
        return (series, x, y);
    }

    // ---- Column charts ---------------------------------------------------------------------

    public static (ISeries[] series, Axis[] x, Axis[] y) ByHour(AnalysisResult r)
    {
        var values = r.PlaytimeByHour.Select(ms => Math.Round(ms / 3_600_000d, 2)).ToArray();
        var labels = Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToArray();
        return Columns(values, labels, "Hours", "Hour of day", Accent);
    }

    public static (ISeries[] series, Axis[] x, Axis[] y) ByDayOfWeek(AnalysisResult r)
    {
        string[] names = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        var values = r.PlaytimeByDayOfWeek.Select(ms => Math.Round(ms / 3_600_000d, 2)).ToArray();
        return Columns(values, names, "Hours", "Day of week", Accent2);
    }

    private static (ISeries[], Axis[], Axis[]) Columns(double[] values, string[] labels, string unit, string xName, SKColor color)
    {
        var series = new ISeries[]
        {
            new ColumnSeries<double>
            {
                Values = values,
                Name = unit,
                Fill = new SolidColorPaint(color),
            },
        };
        var x = new[] { new Axis { Name = xName, Labels = labels, NamePaint = Label(), LabelsPaint = Label() } };
        var y = new[] { new Axis { Name = unit, NamePaint = Label(), LabelsPaint = Label(), MinLimit = 0 } };
        return (series, x, y);
    }

    // ---- Time series -----------------------------------------------------------------------

    public static (ISeries[] series, Axis[] x, Axis[] y) OverTime(AnalysisResult r)
    {
        var points = r.PlaytimeByDay
            .Select(p => new LcDateTimePoint(p.Date, Math.Round(p.Value, 2)))
            .ToArray();

        var series = new ISeries[]
        {
            new LineSeries<LcDateTimePoint>
            {
                Values = points,
                Name = "Hours/day",
                Fill = new SolidColorPaint(Accent.WithAlpha(40)),
                Stroke = new SolidColorPaint(Accent) { StrokeThickness = 2 },
                GeometrySize = 0,
            },
        };
        var x = new[]
        {
            new Axis
            {
                LabelsPaint = Label(),
                Labeler = value =>
                {
                    try { return new DateTime((long)value).ToString("yyyy-MM"); }
                    catch { return string.Empty; }
                },
                UnitWidth = TimeSpan.FromDays(1).Ticks,
            },
        };
        var y = new[] { new Axis { Name = "Hours", NamePaint = Label(), LabelsPaint = Label(), MinLimit = 0 } };
        return (series, x, y);
    }

    // ---- Pie / donut -----------------------------------------------------------------------

    public static ISeries[] ArtistShare(AnalysisResult r)
    {
        var top = r.Artists.Take(10).ToList();
        long topMs = top.Sum(a => a.TotalMsPlayed);
        long otherMs = r.TotalMsPlayed - topMs;

        var series = new List<ISeries>();
        int i = 0;
        foreach (var a in top)
        {
            series.Add(new PieSeries<double>
            {
                Values = new double[] { Math.Round(a.TotalHours, 2) },
                Name = ShortLabel(a.Artist, 18),
                Fill = new SolidColorPaint(Palette[i % Palette.Length]),
                DataLabelsPaint = new SolidColorPaint(Text),
                DataLabelsFormatter = p => ShortLabel(a.Artist, 14),
                InnerRadius = 60,
            });
            i++;
        }

        if (otherMs > 0)
        {
            series.Add(new PieSeries<double>
            {
                Values = new double[] { Math.Round(otherMs / 3_600_000d, 2) },
                Name = "Other",
                Fill = new SolidColorPaint(new SKColor(120, 120, 120)),
                InnerRadius = 60,
            });
        }

        return series.ToArray();
    }
}
