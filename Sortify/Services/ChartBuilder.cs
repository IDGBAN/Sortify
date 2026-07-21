using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using DateTimePoint = Sortify.Models.DateTimePoint;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Sortify.Models;
using LcDateTimePoint = LiveChartsCore.Defaults.DateTimePoint;

namespace Sortify.Services;

/// <summary>Builds LiveCharts2 series and axes from an <see cref="AnalysisResult"/>.</summary>
public static class ChartBuilder
{
    /// <summary>
    /// Absolute safety ceiling on bars drawn in the scrollable horizontal charts. The charts
    /// load more bars as you scroll, but a library with tens of thousands of entries would
    /// eventually build a multi-million-pixel canvas and crash the renderer, so loading stops
    /// here.
    /// </summary>
    public const int MaxBars = 1000;

    private static readonly SKColor Accent = new(29, 185, 84);   // Spotify green
    private static readonly SKColor Accent2 = new(80, 156, 248);
    private static readonly SKColor Text = new(220, 220, 220);

    private static readonly SKColor[] Palette =
    {
        new(29, 185, 84), new(80, 156, 248), new(244, 162, 97), new(231, 111, 81),
        new(42, 157, 143), new(233, 196, 106), new(155, 93, 229), new(247, 37, 133),
        new(76, 201, 240), new(181, 23, 158), new(114, 9, 183), new(58, 134, 255),
        new(255, 159, 28), new(46, 196, 182), new(255, 89, 94), new(124, 200, 60),
        new(255, 196, 61), new(0, 187, 196), new(220, 80, 100), new(147, 130, 220),
        new(72, 191, 145), new(255, 140, 105), new(120, 170, 240), new(210, 130, 215),
        new(176, 205, 80),
    };

    /// <summary>Friendly display names for Spotify "reason_end" codes.</summary>
    private static readonly Dictionary<string, string> ReasonLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trackdone"] = "Track finished",
        ["fwdbtn"] = "Skipped (next)",
        ["backbtn"] = "Back button",
        ["endplay"] = "Playback stopped",
        ["logout"] = "Logged out",
        ["trackerror"] = "Track error",
        ["remote"] = "Remote control",
        ["appload"] = "App reloaded",
        ["popup"] = "Popup",
        ["uriopen"] = "Opened elsewhere",
        ["clickrow"] = "Picked another track",
        ["playbtn"] = "Play button",
        ["clickside"] = "Sidebar click",
        ["unexpected-exit"] = "App exited",
        ["unexpected-exit-while-paused"] = "Exited while paused",
        ["unknown"] = "Unknown",
    };

    private static SolidColorPaint Label() => new(Text);

    private static string ShortLabel(string s, int max = 22)
        => s.Length <= max ? s : s[..(max - 1)] + "…";

    // ---- Horizontal bar charts (RowSeries) -------------------------------------------------

    public static (ISeries[] series, Axis[] x, Axis[] y) TopTracksByTime(AnalysisResult r, int take)
    {
        var items = r.Tracks.Take(take).Reverse().ToList();
        var values = items.Select(t => Math.Round(t.TotalHours, 2)).ToArray();
        var labels = items.Select(t => ShortLabel(t.Track)).ToArray();
        return Rows(values, labels, "Hours", Accent);
    }

    public static (ISeries[] series, Axis[] x, Axis[] y) TopTracksByCount(AnalysisResult r, int take)
    {
        var items = r.TracksByPlayCount.Take(take).Reverse().ToList();
        var values = items.Select(t => (double)t.PlayCount).ToArray();
        var labels = items.Select(t => ShortLabel(t.Track)).ToArray();
        return Rows(values, labels, "Plays", Accent2);
    }

    public static (ISeries[] series, Axis[] x, Axis[] y) TopArtistsByTime(AnalysisResult r, int take)
    {
        var items = r.Artists.Take(take).Reverse().ToList();
        var values = items.Select(a => Math.Round(a.TotalHours, 2)).ToArray();
        var labels = items.Select(a => ShortLabel(a.Artist)).ToArray();
        return Rows(values, labels, "Hours", Accent);
    }

    public static (ISeries[] series, Axis[] x, Axis[] y) TopArtistsByCount(AnalysisResult r, int take)
    {
        var items = r.ArtistsByPlayCount.Take(take).Reverse().ToList();
        var values = items.Select(a => (double)a.PlayCount).ToArray();
        var labels = items.Select(a => ShortLabel(a.Artist)).ToArray();
        return Rows(values, labels, "Plays", Accent2);
    }

    public static (ISeries[] series, Axis[] x, Axis[] y) TopAlbumsByTime(AnalysisResult r, int take)
    {
        var items = r.Albums.Take(take).Reverse().ToList();
        var values = items.Select(a => Math.Round(a.TotalHours, 2)).ToArray();
        var labels = items.Select(a => ShortLabel(a.Album)).ToArray();
        return Rows(values, labels, "Hours", Accent);
    }

    public static (ISeries[] series, Axis[] x, Axis[] y) TopAlbumsByCount(AnalysisResult r, int take)
    {
        var items = r.AlbumsByPlayCount.Take(take).Reverse().ToList();
        var values = items.Select(a => (double)a.PlayCount).ToArray();
        var labels = items.Select(a => ShortLabel(a.Album)).ToArray();
        return Rows(values, labels, "Plays", Accent2);
    }

    public static (ISeries[] series, Axis[] x, Axis[] y) TopSkippedTracks(AnalysisResult r, int take = 15)
    {
        var items = r.SkippedTracks.Take(take).Reverse().ToList();
        var values = items.Select(s => (double)s.SkipCount).ToArray();
        var labels = items.Select(s => ShortLabel(s.Track)).ToArray();
        return Rows(values, labels, "Skips", new SKColor(231, 111, 81));
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
            // One label per bar, smaller text so the category names don't overlap.
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
        // Leave headroom past the longest bar so the right-aligned data label isn't
        // clipped at the chart edge. Wider for bigger numbers (more digits = wider label).
        double maxValue = values.Length > 0 ? values.Max() : 0;
        double maxLimit = maxValue <= 0 ? 1 : maxValue * 1.18;

        var x = new[]
        {
            // The bar values are drawn as data labels on each bar, so the bottom value
            // axis is redundant. Strip its line, ticks and number labels entirely.
            new Axis
            {
                LabelsPaint = null,
                TicksPaint = null,
                SubticksPaint = null,
                SeparatorsPaint = null,
                SubseparatorsPaint = null,
                ZeroPaint = null,
                MinLimit = 0,
                MaxLimit = maxLimit,
            },
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

    /// <summary>Total listening time per calendar year.</summary>
    public static (ISeries[] series, Axis[] x, Axis[] y) HoursPerYear(AnalysisResult r)
    {
        var values = r.Years.Select(y => Math.Round(y.TotalHours, 1)).ToArray();
        var labels = r.Years.Select(y => y.Year.ToString()).ToArray();
        return Columns(values, labels, "Hours", "Year", Accent);
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

    /// <summary>Bucket size for the listening-over-time chart.</summary>
    public enum TimeGranularity { Daily, Weekly, Monthly }

    public static (ISeries[] series, Axis[] x, Axis[] y) OverTime(
        AnalysisResult r, TimeGranularity granularity = TimeGranularity.Daily)
    {
        IEnumerable<DateTimePoint> source = granularity switch
        {
            TimeGranularity.Weekly => r.PlaytimeByDay
                .GroupBy(p => p.Date.AddDays(-(int)p.Date.DayOfWeek))
                .Select(g => new DateTimePoint(g.Key, g.Sum(p => p.Value))),
            TimeGranularity.Monthly => r.PlaytimeByDay
                .GroupBy(p => new DateTime(p.Date.Year, p.Date.Month, 1))
                .Select(g => new DateTimePoint(g.Key, g.Sum(p => p.Value))),
            _ => r.PlaytimeByDay,
        };

        var points = source
            .OrderBy(p => p.Date)
            .Select(p => new LcDateTimePoint(p.Date, Math.Round(p.Value, 2)))
            .ToArray();

        string unitName = granularity switch
        {
            TimeGranularity.Weekly => "Hours/week",
            TimeGranularity.Monthly => "Hours/month",
            _ => "Hours/day",
        };
        long unitTicks = granularity switch
        {
            TimeGranularity.Weekly => TimeSpan.FromDays(7).Ticks,
            TimeGranularity.Monthly => TimeSpan.FromDays(30).Ticks,
            _ => TimeSpan.FromDays(1).Ticks,
        };

        var series = new ISeries[]
        {
            new LineSeries<LcDateTimePoint>
            {
                Values = points,
                Name = unitName,
                Fill = new SolidColorPaint(Accent.WithAlpha(40)),
                Stroke = new SolidColorPaint(Accent) { StrokeThickness = 2 },
                GeometrySize = 0,
            },
        };
        var x = new[] { DateAxis(unitTicks) };
        var y = new[] { new Axis { Name = "Hours", NamePaint = Label(), LabelsPaint = Label(), MinLimit = 0 } };
        return (series, x, y);
    }

    /// <summary>Count of artists heard for the first time, per calendar month.</summary>
    public static (ISeries[] series, Axis[] x, Axis[] y) NewArtistsByMonth(AnalysisResult r)
    {
        var points = r.NewArtistsByMonth
            .Select(p => new LcDateTimePoint(p.Date, p.Value))
            .ToArray();

        var series = new ISeries[]
        {
            new LineSeries<LcDateTimePoint>
            {
                Values = points,
                Name = "New artists",
                Fill = new SolidColorPaint(Accent2.WithAlpha(40)),
                Stroke = new SolidColorPaint(Accent2) { StrokeThickness = 2 },
                GeometrySize = 0,
            },
        };
        var x = new[] { DateAxis(TimeSpan.FromDays(30).Ticks) };
        var y = new[] { new Axis { Name = "New artists", NamePaint = Label(), LabelsPaint = Label(), MinLimit = 0 } };
        return (series, x, y);
    }

    private static Axis DateAxis(long unitTicks) => new()
    {
        LabelsPaint = Label(),
        Labeler = value =>
        {
            try { return new DateTime((long)value).ToString("yyyy-MM"); }
            catch { return string.Empty; }
        },
        UnitWidth = unitTicks,
    };

    // ---- Heatmap ---------------------------------------------------------------------------

    /// <summary>Hour-of-day (x) by day-of-week (y) listening heatmap.</summary>
    public static (ISeries[] series, Axis[] x, Axis[] y) DowHourHeat(AnalysisResult r)
    {
        var points = new List<WeightedPoint>(7 * 24);
        for (int dow = 0; dow < 7; dow++)
            for (int hour = 0; hour < 24; hour++)
                points.Add(new WeightedPoint(hour, dow, Math.Round(r.PlaytimeByDowHour[dow, hour] / 3_600_000d, 2)));

        var series = new ISeries[]
        {
            new HeatSeries<WeightedPoint>
            {
                Values = points,
                Name = "Hours",
                HeatMap = new[]
                {
                    new LvcColor(24, 24, 24),        // no listening: blends into the panel
                    new LvcColor(16, 90, 45),
                    new LvcColor(29, 185, 84),       // Spotify green at the hot end
                    new LvcColor(30, 215, 96),
                },
            },
        };
        string[] dayNames = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        var x = new[]
        {
            new Axis
            {
                Labels = Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToArray(),
                LabelsPaint = Label(),
                TextSize = 10,
            },
        };
        var y = new[]
        {
            new Axis
            {
                Labels = dayNames,
                LabelsPaint = Label(),
                TextSize = 11,
            },
        };
        return (series, x, y);
    }

    // ---- Pie / donut -----------------------------------------------------------------------

    public static ISeries[] ArtistShare(AnalysisResult r)
    {
        var top = r.Artists.Take(25).ToList();
        long topMs = top.Sum(a => a.TotalMsPlayed);
        long otherMs = r.TotalMsPlayed - topMs;
        double totalMs = r.TotalMsPlayed <= 0 ? 1 : r.TotalMsPlayed;

        var series = new List<ISeries>();
        int i = 0;
        foreach (var a in top)
        {
            double hours = Math.Round(a.TotalHours, 2);
            double pct = a.TotalMsPlayed * 100.0 / totalMs;
            // No on-slice labels: they overlap on small slices. Share % lives in the legend instead.
            series.Add(new PieSeries<double>
            {
                Values = new double[] { hours },
                Name = $"{ShortLabel(a.Artist, 22)}  ({pct:0.#}%)",
                Fill = new SolidColorPaint(Palette[i % Palette.Length]),
                ToolTipLabelFormatter = _ => $"{hours:0.#} h  ({pct:0.#}%)",
                InnerRadius = 75,
            });
            i++;
        }

        if (otherMs > 0)
        {
            double oHours = Math.Round(otherMs / 3_600_000d, 2);
            double oPct = otherMs * 100.0 / totalMs;
            series.Add(new PieSeries<double>
            {
                Values = new double[] { oHours },
                Name = $"Other  ({oPct:0.#}%)",
                Fill = new SolidColorPaint(new SKColor(110, 110, 110)),
                ToolTipLabelFormatter = _ => $"{oHours:0.#} h  ({oPct:0.#}%)",
                InnerRadius = 75,
            });
        }

        return series.ToArray();
    }

    /// <summary>Donut of why plays ended (reason_end), top reasons plus "Other".</summary>
    public static ISeries[] ReasonEndShare(AnalysisResult r)
    {
        const int maxSlices = 8;
        long total = r.ReasonEnds.Sum(x => (long)x.Count);
        if (total <= 0)
            return Array.Empty<ISeries>();

        var series = new List<ISeries>();
        int i = 0;
        foreach (var reason in r.ReasonEnds.Take(maxSlices))
        {
            string label = ReasonLabels.TryGetValue(reason.Reason, out var friendly)
                ? friendly
                : reason.Reason;
            AddCountSlice(series, label, reason.Count, total, Palette[i % Palette.Length]);
            i++;
        }

        int otherCount = r.ReasonEnds.Skip(maxSlices).Sum(x => x.Count);
        if (otherCount > 0)
            AddCountSlice(series, "Other", otherCount, total, new SKColor(110, 110, 110));

        return series.ToArray();
    }

    /// <summary>Donut of listening time by device family.</summary>
    public static ISeries[] PlatformShare(AnalysisResult r) => ContextShare(r.Platforms, maxSlices: 8);

    /// <summary>Donut of listening time by the country each play streamed from.</summary>
    public static ISeries[] CountryShare(AnalysisResult r) => ContextShare(r.Countries, maxSlices: 10);

    private static ISeries[] ContextShare(IReadOnlyList<ContextStat> stats, int maxSlices)
    {
        long total = stats.Sum(s => s.TotalMsPlayed);
        if (total <= 0)
            return Array.Empty<ISeries>();

        var series = new List<ISeries>();
        int i = 0;
        foreach (var stat in stats.Take(maxSlices))
        {
            AddHoursSlice(series, stat.Name, stat.TotalMsPlayed, total, Palette[i % Palette.Length]);
            i++;
        }

        long otherMs = stats.Skip(maxSlices).Sum(s => s.TotalMsPlayed);
        if (otherMs > 0)
            AddHoursSlice(series, "Other", otherMs, total, new SKColor(110, 110, 110));

        return series.ToArray();
    }

    private static void AddHoursSlice(List<ISeries> series, string label, long ms, long total, SKColor color)
    {
        double hours = Math.Round(ms / 3_600_000d, 2);
        double pct = ms * 100.0 / total;
        series.Add(new PieSeries<double>
        {
            Values = new double[] { hours },
            Name = $"{ShortLabel(label, 22)}  ({pct:0.#}%)",
            Fill = new SolidColorPaint(color),
            ToolTipLabelFormatter = _ => $"{hours:0.#} h  ({pct:0.#}%)",
            InnerRadius = 60,
        });
    }

    // ---- Drill-down detail --------------------------------------------------------------------

    /// <summary>Monthly listening time for a single artist, track or album.</summary>
    public static (ISeries[] series, Axis[] x, Axis[] y) DetailByMonth(DetailResult d)
    {
        var points = d.ByMonth
            .Select(p => new LcDateTimePoint(p.Date, Math.Round(p.Value, 2)))
            .ToArray();

        var series = new ISeries[]
        {
            new ColumnSeries<LcDateTimePoint>
            {
                Values = points,
                Name = "Hours",
                Fill = new SolidColorPaint(Accent),
            },
        };
        var x = new[]
        {
            new Axis
            {
                Labeler = value => new DateTime((long)value).ToString("yyyy-MM"),
                UnitWidth = TimeSpan.FromDays(30).Ticks,
                LabelsPaint = Label(),
                TextSize = 11,
            },
        };
        var y = new[] { new Axis { Name = "Hours", NamePaint = Label(), LabelsPaint = Label(), MinLimit = 0 } };
        return (series, x, y);
    }

    /// <summary>Hour-of-day profile for a single artist, track or album.</summary>
    public static (ISeries[] series, Axis[] x, Axis[] y) DetailByHour(DetailResult d)
    {
        var values = d.ByHour.Select(ms => Math.Round(ms / 3_600_000d, 2)).ToArray();
        var labels = Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToArray();
        return Columns(values, labels, "Hours", "Hour of day", Accent2);
    }

    // ---- Podcasts ----------------------------------------------------------------------------

    public static (ISeries[] series, Axis[] x, Axis[] y) TopShows(AnalysisResult r, int take = 20)
    {
        var items = r.Shows.Take(take).Reverse().ToList();
        var values = items.Select(s => Math.Round(s.TotalHours, 2)).ToArray();
        var labels = items.Select(s => ShortLabel(s.Show)).ToArray();
        return Rows(values, labels, "Hours", new SKColor(155, 93, 229));
    }

    public static (ISeries[] series, Axis[] x, Axis[] y) TopEpisodes(AnalysisResult r, int take = 20)
    {
        var items = r.Episodes.Take(take).Reverse().ToList();
        var values = items.Select(e => Math.Round(e.TotalHours, 2)).ToArray();
        var labels = items.Select(e => ShortLabel(e.Episode)).ToArray();
        return Rows(values, labels, "Hours", new SKColor(76, 201, 240));
    }

    private static void AddCountSlice(List<ISeries> series, string label, int count, long total, SKColor color)
    {
        double pct = count * 100.0 / total;
        series.Add(new PieSeries<double>
        {
            Values = new double[] { count },
            Name = $"{label}  ({pct:0.#}%)",
            Fill = new SolidColorPaint(color),
            ToolTipLabelFormatter = _ => $"{count:N0} plays  ({pct:0.#}%)",
            InnerRadius = 60,
        });
    }
}
