using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Sortify.Models;
// Sortify.Models has a DateTimePoint too, so LiveCharts' goes by another name.
using LcDateTimePoint = LiveChartsCore.Defaults.DateTimePoint;

namespace Sortify.Services;

/// <summary>
/// Builds LiveCharts2 series and axes from an <see cref="AnalysisResult"/>. Colours come
/// from <see cref="ChartPalette"/>, so every chart follows the active theme once it is
/// rebuilt.
/// </summary>
public static class ChartBuilder
{
    /// <summary>
    /// Absolute safety ceiling on bars drawn in the scrollable horizontal charts. The charts
    /// load more bars as you scroll, but a library with tens of thousands of entries would
    /// eventually build a multi-million-pixel canvas and crash the renderer, so loading stops
    /// here.
    /// </summary>
    public const int MaxBars = 1000;

    /// <summary>Bar and axis labels longer than this are cut short with an ellipsis.</summary>
    private const int LabelMaxChars = 22;

    /// <summary>Named artists in the Overview donut; everyone else is folded into "Other".</summary>
    public const int ArtistShareSlices = 25;

    public const int SkippedTrackBars = 15;
    public const int PodcastBars = 20;

    private const int ReasonSlices = 8;
    private const int PlatformSlices = 8;
    private const int CountrySlices = 10;

    // Axis text in pixels. LiveCharts defaults to 16 for labels and 20 for axis titles, which
    // shouts next to the 11-13px UI text around the charts.
    private const double AxisTextSize = 12;
    private const double DenseAxisTextSize = 11;

    // Month-labelled axes never step by less than this. Anything shorter than the longest
    // month can land twice in one month and print the same "yyyy-MM" label twice.
    private static readonly long MonthStepTicks = TimeSpan.FromDays(31).Ticks;

    // Donut hole radii in pixels. The artist donut is drawn larger, so it gets a bigger hole.
    private const double ArtistDonutInnerRadius = 75;
    private const double ContextDonutInnerRadius = 60;

    private static SolidColorPaint Label() => new(ChartPalette.Text);

    private static SolidColorPaint RankLabel() => new(ChartPalette.Text)
    {
        SKTypeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold),
    };

    /// <summary>
    /// Rank numbers for a bar chart of <paramref name="count"/> bars, bottom bar first: the
    /// bars run bottom-up, so the top one, ranked 1, is last.
    /// </summary>
    internal static string[] RankLabels(int count) =>
        Enumerable.Range(1, count).Reverse().Select(rank => rank.ToString()).ToArray();

    internal static string ShortLabel(string s, int max = LabelMaxChars)
    {
        if (s.Length <= max)
            return s;

        // Don't cut an emoji (or any other surrogate pair) in half; the orphaned half renders
        // as a replacement box.
        int cut = max - 1;
        if (char.IsHighSurrogate(s[cut - 1]))
            cut--;
        return s[..cut] + "…";
    }

    // ---- Horizontal bar charts (RowSeries) -------------------------------------------------

    public static ChartData TopTracks(AnalysisResult r, int take, Metric metric) =>
        metric == Metric.Plays ? TopTracksByCount(r, take) : TopTracksByTime(r, take);

    public static ChartData TopArtists(AnalysisResult r, int take, Metric metric) =>
        metric == Metric.Plays ? TopArtistsByCount(r, take) : TopArtistsByTime(r, take);

    public static ChartData TopAlbums(AnalysisResult r, int take, Metric metric) =>
        metric == Metric.Plays ? TopAlbumsByCount(r, take) : TopAlbumsByTime(r, take);

    public static ChartData TopTracksByTime(AnalysisResult r, int take)
    {
        var items = r.Tracks.Take(take).Reverse().ToList();
        return Rows(
            items.Select(t => Math.Round(t.TotalHours, 2)).ToArray(),
            items.Select(t => ShortLabel(t.Track)).ToArray(),
            "Hours", ChartPalette.Accent, items);
    }

    public static ChartData TopTracksByCount(AnalysisResult r, int take)
    {
        var items = r.TracksByPlayCount.Take(take).Reverse().ToList();
        return Rows(
            items.Select(t => (double)t.PlayCount).ToArray(),
            items.Select(t => ShortLabel(t.Track)).ToArray(),
            "Plays", ChartPalette.Accent2, items);
    }

    public static ChartData TopArtistsByTime(AnalysisResult r, int take)
    {
        var items = r.Artists.Take(take).Reverse().ToList();
        return Rows(
            items.Select(a => Math.Round(a.TotalHours, 2)).ToArray(),
            items.Select(a => ShortLabel(a.Artist)).ToArray(),
            "Hours", ChartPalette.Accent, items);
    }

    public static ChartData TopArtistsByCount(AnalysisResult r, int take)
    {
        var items = r.ArtistsByPlayCount.Take(take).Reverse().ToList();
        return Rows(
            items.Select(a => (double)a.PlayCount).ToArray(),
            items.Select(a => ShortLabel(a.Artist)).ToArray(),
            "Plays", ChartPalette.Accent2, items);
    }

    public static ChartData TopAlbumsByTime(AnalysisResult r, int take)
    {
        var items = r.Albums.Take(take).Reverse().ToList();
        return Rows(
            items.Select(a => Math.Round(a.TotalHours, 2)).ToArray(),
            items.Select(a => ShortLabel(a.Album)).ToArray(),
            "Hours", ChartPalette.Accent, items);
    }

    public static ChartData TopAlbumsByCount(AnalysisResult r, int take)
    {
        var items = r.AlbumsByPlayCount.Take(take).Reverse().ToList();
        return Rows(
            items.Select(a => (double)a.PlayCount).ToArray(),
            items.Select(a => ShortLabel(a.Album)).ToArray(),
            "Plays", ChartPalette.Accent2, items);
    }

    public static ChartData TopSkippedTracks(AnalysisResult r, int take = SkippedTrackBars)
    {
        var items = r.SkippedTracks.Take(take).Reverse().ToList();
        return Rows(
            items.Select(s => (double)s.SkipCount).ToArray(),
            items.Select(s => ShortLabel(s.Track)).ToArray(),
            "Skips", ChartPalette.Warm, items);
    }

    private static ChartData Rows<T>(double[] values, string[] labels, string unit, SKColor color, IReadOnlyList<T> items)
        where T : class
    {
        var series = new ISeries[]
        {
            new RowSeries<double>
            {
                Values = values,
                Name = unit,
                Fill = new SolidColorPaint(color),
                DataLabelsPaint = new SolidColorPaint(ChartPalette.Text),
                DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Right,
                DataLabelsFormatter = p => p.Coordinate.PrimaryValue.ToString("0.##"),
                DataLabelsSize = 11,
                Padding = 2,
                // Scaled against the name axis; the rank axis before it only carries labels.
                ScalesYAt = 1,
            },
        };
        // Both axes are pinned to the same span, one unit per bar, so each rank lines up
        // with its name; the rank axis has no series of its own to size it.
        double minRow = -0.5;
        double maxRow = Math.Max(values.Length, 1) - 0.5;
        var y = new[]
        {
            // The rank, in its own axis outside the names rather than glued onto them.
            // Bars are drawn bottom-up, so the first bar is the last-ranked one.
            new Axis
            {
                Labels = RankLabels(labels.Length),
                LabelsPaint = RankLabel(),
                TextSize = DenseAxisTextSize,
                MinStep = 1,
                ForceStepToMin = true,
                SeparatorsPaint = null,
                MinLimit = minRow,
                MaxLimit = maxRow,
            },
            // One label per bar, smaller text so the category names don't overlap.
            new Axis
            {
                Labels = labels,
                LabelsPaint = Label(),
                TextSize = DenseAxisTextSize,
                MinStep = 1,
                ForceStepToMin = true,
                SeparatorsPaint = null,
                MinLimit = minRow,
                MaxLimit = maxRow,
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
        return new ChartData { Series = series, XAxes = x, YAxes = y, Items = items };
    }

    // ---- Column charts ---------------------------------------------------------------------

    public static ChartData ByHour(AnalysisResult r, Metric metric = Metric.Time)
    {
        var values = metric == Metric.Plays
            ? r.PlaysByHour.Select(n => (double)n).ToArray()
            : r.PlaytimeByHour.Select(ms => Math.Round(ms / 3_600_000d, 2)).ToArray();
        var labels = Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToArray();
        return Columns(values, labels, Unit(metric), "Hour of day", ChartPalette.Accent);
    }

    /// <summary>Short day names, Sunday first, as <see cref="DayOfWeek"/> numbers them.</summary>
    private static readonly string[] DayNames = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

    /// <summary>Listening time, or plays, per day of the week, starting from <paramref name="firstDay"/>.</summary>
    public static ChartData ByDayOfWeek(
        AnalysisResult r, Metric metric = Metric.Time, DayOfWeek firstDay = DayOfWeek.Sunday)
    {
        var days = Weeks.Ordered(firstDay);
        var values = days
            .Select(d => metric == Metric.Plays
                ? r.PlaysByDayOfWeek[(int)d]
                : Math.Round(r.PlaytimeByDayOfWeek[(int)d] / 3_600_000d, 2))
            .ToArray();
        return Columns(values, days.Select(d => DayNames[(int)d]).ToArray(), Unit(metric), "Day of week", ChartPalette.Accent2);
    }

    /// <summary>Total listening time, or plays, per calendar year.</summary>
    public static ChartData PerYear(AnalysisResult r, Metric metric = Metric.Time)
    {
        var values = r.Years
            .Select(y => metric == Metric.Plays ? y.PlayCount : Math.Round(y.TotalHours, 1))
            .ToArray();
        var labels = r.Years.Select(y => y.Year.ToString()).ToArray();
        return Columns(values, labels, Unit(metric), "Year", ChartPalette.Accent, r.Years);
    }

    /// <summary>
    /// Each year's listening stacked by how long before it the music was first heard: that
    /// year at the bottom, then a band for each of the <see cref="DiscoveryCohorts.YearsBack"/>
    /// years before, and everything older on top. Relative years rather than calendar ones, so
    /// a twentieth year reads as clearly as a second. Empty bands are left out. The columns' items are the matching rows
    /// of <paramref name="years"/>, so a click opens that year.
    /// </summary>
    public static ChartData Cohorts(CohortBreakdown c, IReadOnlyList<YearStat> years)
    {
        int bands = DiscoveryCohorts.YearsBack + 1;
        var hours = new double[bands + 1, c.Years.Count];
        for (int y = 0; y < c.Years.Count; y++)
        {
            for (int j = 0; j < c.Cohorts.Count; j++)
            {
                // Music can't be first heard after it was played, so those cells are empty.
                int age = c.Years[y] - c.Cohorts[j];
                if (age >= 0)
                    hours[Math.Min(age, bands), y] += c.Hours[y, j];
            }
        }

        double[] Band(int age) => Enumerable.Range(0, c.Years.Count).Select(y => Math.Round(hours[age, y], 1)).ToArray();

        // New music at the bottom, so its share is measured from the same baseline every year.
        var series = new List<ISeries>();
        for (int age = 0; age <= bands; age++)
        {
            var values = Band(age);
            if (values.All(v => v == 0))
                continue;

            string name = age switch
            {
                0 => "Found that year",
                1 => "Found the year before",
                _ when age == bands => "Found earlier",
                _ => $"Found {age} years before",
            };
            series.Add(Stack(values, name, age == bands ? ChartPalette.Muted : Category(age)));
        }

        var byYear = years.ToDictionary(y => y.Year);
        return new ChartData
        {
            Series = series.ToArray(),
            XAxes = new[]
            {
                new Axis
                {
                    Name = "Year",
                    Labels = c.Years.Select(y => y.ToString()).ToArray(),
                    NamePaint = Label(),
                    NameTextSize = AxisTextSize,
                    LabelsPaint = Label(),
                    TextSize = AxisTextSize,
                },
            },
            YAxes = new[] { ValueAxis("Hours") },
            Items = c.Years.Select(y => byYear.GetValueOrDefault(y) ?? new YearStat { Year = y }).ToList(),
        };

        static ISeries Stack(double[] values, string name, SKColor color) => new StackedColumnSeries<double>
        {
            Values = values,
            Name = name,
            Fill = new SolidColorPaint(color),
        };
    }

    /// <summary>
    /// The top artists' rank in each year, one line each, #1 at the top. A year an artist sat
    /// below the floor is a gap in their line. The chart's items are the artists in series
    /// order, so a click on a point can open its artist.
    /// </summary>
    public static ChartData RankHistory(AnalysisResult r)
    {
        var series = r.RankHistory
            .Select((a, i) => (ISeries)new LineSeries<double?>
            {
                Values = a.Ranks.Select(rank => (double?)rank).ToArray(),
                Name = ShortLabel(a.Artist.Artist),
                Fill = null,
                Stroke = new SolidColorPaint(Category(i)) { StrokeThickness = 3 },
                GeometrySize = 10,
                GeometryFill = new SolidColorPaint(Category(i)),
                GeometryStroke = new SolidColorPaint(Category(i)) { StrokeThickness = 2 },
                LineSmoothness = 0,
                YToolTipLabelFormatter = p => $"#{p.Coordinate.PrimaryValue:0}",
            })
            .ToArray();

        int lowest = r.RankHistory.SelectMany(a => a.Ranks).Max() ?? 1;
        return new ChartData
        {
            Series = series,
            XAxes = new[]
            {
                new Axis
                {
                    Name = "Year",
                    NamePaint = Label(),
                    NameTextSize = AxisTextSize,
                    Labels = r.Years.Select(y => y.Year.ToString()).ToArray(),
                    LabelsPaint = Label(),
                    TextSize = AxisTextSize,
                    MinStep = 1,
                },
            },
            YAxes = new[]
            {
                new Axis
                {
                    Name = "Rank",
                    NamePaint = Label(),
                    NameTextSize = AxisTextSize,
                    LabelsPaint = Label(),
                    TextSize = AxisTextSize,
                    IsInverted = true,
                    MinLimit = 0.5,
                    MaxLimit = lowest + 0.5,
                    MinStep = 1,
                    ForceStepToMin = true,
                    Labeler = value => value >= 1 ? $"#{value:0}" : string.Empty,
                },
            },
            Items = r.RankHistory.Select(a => a.Artist).ToList(),
            ItemsFollowSeries = true,
        };
    }
    /// <summary>What a value axis and its series are called: "Hours" or "Plays".</summary>
    private static string Unit(Metric metric) => metric == Metric.Plays ? "Plays" : "Hours";

    private static ChartData Columns(
        double[] values, string[] labels, string unit, string xName, SKColor color, IReadOnlyList<object>? items = null)
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
        return new ChartData
        {
            Series = series,
            XAxes = new[]
            {
                new Axis
                {
                    Name = xName,
                    Labels = labels,
                    NamePaint = Label(),
                    NameTextSize = AxisTextSize,
                    LabelsPaint = Label(),
                    TextSize = AxisTextSize,
                },
            },
            YAxes = new[] { ValueAxis(unit) },
            Items = items ?? Array.Empty<object>(),
        };
    }

    // ---- Time series -----------------------------------------------------------------------

    /// <summary>Bucket size for the listening-over-time chart.</summary>
    public enum TimeGranularity { Daily, Weekly, Monthly }

    /// <summary>
    /// Listening time, or plays, per day, week or month from the first play to the last. A
    /// stretch with nothing played is a run of zeros, so the line drops to the floor across a break.
    /// </summary>
    /// <param name="firstDay">The day a weekly bucket starts on.</param>
    public static ChartData OverTime(
        AnalysisResult r, TimeGranularity granularity = TimeGranularity.Daily, Metric metric = Metric.Time,
        DayOfWeek firstDay = DayOfWeek.Sunday)
    {
        var byDay = metric == Metric.Plays ? r.PlaysByDay : r.PlaytimeByDay;
        var totals = new Dictionary<DateTime, double>();
        foreach (var day in byDay)
        {
            var bucket = TimeBuckets.Of(day.Date, granularity, firstDay);
            totals[bucket] = totals.GetValueOrDefault(bucket) + day.Value;
        }

        var points = byDay.Count == 0
            ? Array.Empty<LcDateTimePoint>()
            : TimeBuckets.Between(byDay.Min(p => p.Date), byDay.Max(p => p.Date), granularity, firstDay)
                .Select(bucket => new LcDateTimePoint(bucket, Math.Round(totals.GetValueOrDefault(bucket), 2)))
                .ToArray();

        string unitName = Unit(metric) + granularity switch
        {
            TimeGranularity.Weekly => "/week",
            TimeGranularity.Monthly => "/month",
            _ => "/day",
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
                Fill = new SolidColorPaint(ChartPalette.Accent.WithAlpha(40)),
                Stroke = new SolidColorPaint(ChartPalette.Accent) { StrokeThickness = 2 },
                GeometrySize = 0,
                // Straight, as in the artist timelines: a curve through the zeros would dip below the floor.
                LineSmoothness = 0,
            },
        };
        return new ChartData
        {
            Series = series,
            XAxes = new[] { DateAxis(unitTicks) },
            YAxes = new[] { ValueAxis(Unit(metric)) },
        };
    }

    /// <summary>Count of artists heard for the first time, per calendar month.</summary>
    public static ChartData NewArtistsByMonth(AnalysisResult r)
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
                Fill = new SolidColorPaint(ChartPalette.Accent2.WithAlpha(40)),
                Stroke = new SolidColorPaint(ChartPalette.Accent2) { StrokeThickness = 2 },
                GeometrySize = 0,
                LineSmoothness = 0,
            },
        };
        return new ChartData
        {
            Series = series,
            XAxes = new[] { DateAxis(TimeSpan.FromDays(30).Ticks) },
            YAxes = new[] { ValueAxis("New artists") },
        };
    }

    /// <summary>One line per compared artist, in the category colours, on a shared date axis.</summary>
    public static ChartData ArtistTimelines(ArtistTimeline timeline, TimeGranularity granularity, Metric metric = Metric.Time)
    {
        IReadOnlyList<(string Artist, double[] Values)> lines = metric == Metric.Plays ? timeline.PlaySeries : timeline.Series;
        var series = lines
            .Select((s, i) => (ISeries)new LineSeries<LcDateTimePoint>
            {
                Values = timeline.Buckets.Select((b, j) => new LcDateTimePoint(b, s.Values.ElementAtOrDefault(j))).ToArray(),
                Name = ShortLabel(s.Artist),
                Fill = null,
                Stroke = new SolidColorPaint(Category(i)) { StrokeThickness = 2 },
                GeometrySize = 0,
                // Straight segments: smoothing a line that drops to zero between busy months
                // would swing it below zero and back.
                LineSmoothness = 0,
            })
            .ToArray();

        long unitTicks = granularity == TimeGranularity.Weekly
            ? TimeSpan.FromDays(7).Ticks
            : TimeSpan.FromDays(30).Ticks;
        return new ChartData
        {
            Series = series,
            XAxes = new[] { DateAxis(unitTicks) },
            YAxes = new[] { ValueAxis(Unit(metric)) },
        };
    }

    private static Axis DateAxis(long unitTicks) => new()
    {
        LabelsPaint = Label(),
        TextSize = AxisTextSize,
        Labeler = MonthLabel,
        UnitWidth = unitTicks,
        MinStep = MonthStepTicks,
    };

    private static Axis ValueAxis(string name) => new()
    {
        Name = name,
        NamePaint = Label(),
        NameTextSize = AxisTextSize,
        LabelsPaint = Label(),
        TextSize = AxisTextSize,
        MinLimit = 0,
    };

    /// <summary>
    /// Formats an axis value that carries DateTime ticks. LiveCharts asks for labels beyond
    /// the data range while a chart animates or the user pans, and those can fall outside
    /// what a DateTime can hold, so out-of-range values get no label rather than throwing.
    /// </summary>
    private static string MonthLabel(double value)
    {
        if (value < DateTime.MinValue.Ticks || value > DateTime.MaxValue.Ticks)
            return string.Empty;
        return TimeFormat.Month(new DateTime((long)value));
    }

    // ---- Heatmap ---------------------------------------------------------------------------

    /// <summary>
    /// Hour-of-day (x) by day-of-week (y) heatmap of listening time, or plays, its rows starting
    /// from <paramref name="firstDay"/>. Each square's day and hour ride along as the chart's
    /// items, so a click can be traced back to them whatever the order.
    /// </summary>
    public static ChartData DowHourHeat(
        AnalysisResult r, Metric metric = Metric.Time, DayOfWeek firstDay = DayOfWeek.Sunday)
    {
        var days = Weeks.Ordered(firstDay);
        var points = new List<WeightedPoint>(7 * 24);
        var slots = new List<HeatSlot>(7 * 24);
        for (int row = 0; row < 7; row++)
        {
            for (int hour = 0; hour < 24; hour++)
            {
                int dow = (int)days[row];
                double weight = metric == Metric.Plays
                    ? r.PlaysByDowHour[dow, hour]
                    : Math.Round(r.PlaytimeByDowHour[dow, hour] / 3_600_000d, 2);
                points.Add(new WeightedPoint(hour, row, weight));
                slots.Add(new HeatSlot(days[row], hour));
            }
        }

        var series = new ISeries[]
        {
            new HeatSeries<WeightedPoint>
            {
                Values = points,
                Name = Unit(metric),
                HeatMap = ChartPalette.HeatRamp
                    .Select(c => new LvcColor(c.Red, c.Green, c.Blue))
                    .ToArray(),
            },
        };
        return new ChartData
        {
            Series = series,
            Items = slots,
            XAxes = new[]
            {
                new Axis
                {
                    Labels = Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToArray(),
                    LabelsPaint = Label(),
                    TextSize = DenseAxisTextSize,
                    MinStep = 1,
                    ForceStepToMin = true,
                },
            },
            YAxes = new[]
            {
                new Axis
                {
                    Labels = days.Select(d => DayNames[(int)d]).ToArray(),
                    LabelsPaint = Label(),
                    TextSize = DenseAxisTextSize,
                    // Without this LiveCharts labels every other row and leaves Mon, Wed
                    // and Fri blank.
                    MinStep = 1,
                    ForceStepToMin = true,
                },
            },
        };
    }

    /// <summary>One square of the heatmap: an hour of a day of the week.</summary>
    public sealed record HeatSlot(DayOfWeek Day, int Hour);

    // ---- Pie / donut -----------------------------------------------------------------------

    /// <summary>
    /// One slice of a donut: its rank (null for "Other"), name, value, share and colour, and
    /// what its tooltip says. A donut and its legend are both built from these, so the legend
    /// can show the rank in a column of its own rather than in the name.
    /// </summary>
    public sealed record ShareSlice(int? Rank, string Name, double Value, double Percent, SKColor Color, string Tooltip);

    /// <summary>The top artists by listening time, or plays, then everyone else folded into "Other".</summary>
    public static IReadOnlyList<ShareSlice> ArtistShareSlicesOf(AnalysisResult r, Metric metric = Metric.Time)
    {
        bool plays = metric == Metric.Plays;
        var top = (plays ? r.ArtistsByPlayCount : r.Artists).Take(ArtistShareSlices).ToList();
        long Amount(ArtistStat a) => plays ? a.PlayCount : a.TotalMsPlayed;
        long total = plays ? r.TotalPlays : r.TotalMsPlayed;
        long other = total - top.Sum(Amount);
        total = Math.Max(1, total);

        var slices = top.Select((a, i) => Slice(i + 1, a.Artist, Amount(a), total, Category(i), metric)).ToList();
        if (other > 0)
            slices.Add(Slice(null, "Other", other, total, ChartPalette.Muted, metric));
        return slices;
    }

    /// <summary>Why plays ended (reason_end): the top reasons plus "Other".</summary>
    public static IReadOnlyList<ShareSlice> ReasonEndSlicesOf(AnalysisResult r)
    {
        long total = r.ReasonEnds.Sum(x => (long)x.Count);
        if (total <= 0)
            return Array.Empty<ShareSlice>();

        var slices = r.ReasonEnds
            .Take(ReasonSlices)
            .Select((reason, i) => CountSlice(i + 1, EndReasons.Label(reason.Reason), reason.Count, total, Category(i)))
            .ToList();

        int otherCount = r.ReasonEnds.Skip(ReasonSlices).Sum(x => x.Count);
        if (otherCount > 0)
            slices.Add(CountSlice(null, "Other", otherCount, total, ChartPalette.Muted));
        return slices;
    }

    /// <summary>Listening time, or plays, by device family.</summary>
    public static IReadOnlyList<ShareSlice> PlatformSlicesOf(AnalysisResult r, Metric metric = Metric.Time) =>
        ContextSlices(r.Platforms, PlatformSlices, metric);

    /// <summary>Listening time, or plays, by the country each play streamed from.</summary>
    public static IReadOnlyList<ShareSlice> CountrySlicesOf(AnalysisResult r, Metric metric = Metric.Time) =>
        ContextSlices(r.Countries, CountrySlices, metric);

    public static ISeries[] ArtistShare(AnalysisResult r, Metric metric = Metric.Time) =>
        Donut(ArtistShareSlicesOf(r, metric), ArtistDonutInnerRadius, "Artist", Unit(metric));

    /// <summary>Donut of why plays ended (reason_end), top reasons plus "Other".</summary>
    public static ISeries[] ReasonEndShare(AnalysisResult r) =>
        Donut(ReasonEndSlicesOf(r), ContextDonutInnerRadius, "Reason", Unit(Metric.Plays));

    /// <summary>Donut of listening time, or plays, by device family.</summary>
    public static ISeries[] PlatformShare(AnalysisResult r, Metric metric = Metric.Time) =>
        Donut(PlatformSlicesOf(r, metric), ContextDonutInnerRadius, "Device", Unit(metric));

    /// <summary>Donut of listening time, or plays, by the country each play streamed from.</summary>
    public static ISeries[] CountryShare(AnalysisResult r, Metric metric = Metric.Time) =>
        Donut(CountrySlicesOf(r, metric), ContextDonutInnerRadius, "Country", Unit(metric));

    private static SKColor Category(int index)
    {
        var categories = ChartPalette.Categories;
        return categories[index % categories.Length];
    }

    private static IReadOnlyList<ShareSlice> ContextSlices(IReadOnlyList<ContextStat> stats, int maxSlices, Metric metric)
    {
        // The stats arrive in listening-time order; there are only a few dozen at most.
        long Amount(ContextStat s) => metric == Metric.Plays ? s.PlayCount : s.TotalMsPlayed;
        var ranked = metric == Metric.Plays ? stats.OrderByDescending(s => s.PlayCount).ToList() : stats;

        long total = ranked.Sum(Amount);
        if (total <= 0)
            return Array.Empty<ShareSlice>();

        var slices = ranked
            .Take(maxSlices)
            .Select((stat, i) => Slice(i + 1, stat.Name, Amount(stat), total, Category(i), metric))
            .ToList();

        long other = ranked.Skip(maxSlices).Sum(Amount);
        if (other > 0)
            slices.Add(Slice(null, "Other", other, total, ChartPalette.Muted, metric));
        return slices;
    }

    /// <summary>A donut slice of listening time (<paramref name="amount"/> in ms) or of plays.</summary>
    private static ShareSlice Slice(int? rank, string name, long amount, long total, SKColor color, Metric metric) =>
        metric == Metric.Plays
            ? CountSlice(rank, name, (int)amount, total, color)
            : HoursSlice(rank, name, amount, total, color);

    private static ShareSlice HoursSlice(int? rank, string name, long ms, long total, SKColor color)
    {
        double hours = Math.Round(ms / 3_600_000d, 2);
        double pct = ms * 100.0 / total;
        return new ShareSlice(rank, name, hours, pct, color, $"{hours:0.#} h  ({pct:0.#}%)");
    }

    private static ShareSlice CountSlice(int? rank, string name, int count, long total, SKColor color)
    {
        double pct = count * 100.0 / total;
        return new ShareSlice(rank, name, count, pct, color, $"{Plural.Of(count, "play")}  ({pct:0.#}%)");
    }

    // No on-slice labels: they overlap on small slices. The chart's own legend is hidden too;
    // the rank, name and share % live in the legend beside it (see MainViewModel's *Legend).
    // The tag keeps the full name and share for the chart's table of numbers.
    private static ISeries[] Donut(IReadOnlyList<ShareSlice> slices, double innerRadius, string kind, string unit) => slices
        .Select(s => (ISeries)new PieSeries<double>
        {
            Values = new[] { s.Value },
            Name = ShortLabel(s.Name),
            Tag = new ChartSlice(s.Name, kind, unit, s.Percent),
            Fill = new SolidColorPaint(s.Color),
            ToolTipLabelFormatter = _ => s.Tooltip,
            InnerRadius = innerRadius,
        })
        .ToArray();

    // ---- Drill-down detail --------------------------------------------------------------------

    /// <summary>Monthly listening time, or plays, for a single artist, track, album or year.</summary>
    public static ChartData DetailByMonth(DetailResult d, Metric metric = Metric.Time)
    {
        var points = (metric == Metric.Plays ? d.PlaysByMonth : d.ByMonth)
            .Select(p => new LcDateTimePoint(p.Date, Math.Round(p.Value, 2)))
            .ToArray();

        var series = new ISeries[]
        {
            new ColumnSeries<LcDateTimePoint>
            {
                Values = points,
                Name = Unit(metric),
                Fill = new SolidColorPaint(ChartPalette.Accent),
            },
        };
        return new ChartData
        {
            Series = series,
            XAxes = new[]
            {
                new Axis
                {
                    Labeler = MonthLabel,
                    UnitWidth = TimeSpan.FromDays(30).Ticks,
                    MinStep = MonthStepTicks,
                    LabelsPaint = Label(),
                    TextSize = AxisTextSize,
                },
            },
            YAxes = new[] { ValueAxis(Unit(metric)) },
        };
    }

    /// <summary>Hour-of-day profile for a single artist, track, album or year.</summary>
    public static ChartData DetailByHour(DetailResult d, Metric metric = Metric.Time)
    {
        var values = metric == Metric.Plays
            ? d.PlaysByHour.Select(n => (double)n).ToArray()
            : d.ByHour.Select(ms => Math.Round(ms / 3_600_000d, 2)).ToArray();
        var labels = Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToArray();
        return Columns(values, labels, Unit(metric), "Hour of day", ChartPalette.Accent2);
    }

    // ---- Podcasts ----------------------------------------------------------------------------

    public static ChartData TopShows(AnalysisResult r, int take = PodcastBars, Metric metric = Metric.Time)
    {
        bool plays = metric == Metric.Plays;
        var items = (plays ? r.ShowsByPlayCount : r.Shows).Take(take).Reverse().ToList();
        return Rows(
            items.Select(s => plays ? s.PlayCount : Math.Round(s.TotalHours, 2)).ToArray(),
            items.Select(s => ShortLabel(s.Show)).ToArray(),
            Unit(metric), ChartPalette.Violet, items);
    }

    public static ChartData TopEpisodes(AnalysisResult r, int take = PodcastBars, Metric metric = Metric.Time)
    {
        bool plays = metric == Metric.Plays;
        var items = (plays ? r.EpisodesByPlayCount : r.Episodes).Take(take).Reverse().ToList();
        return Rows(
            items.Select(e => plays ? e.PlayCount : Math.Round(e.TotalHours, 2)).ToArray(),
            items.Select(e => ShortLabel(e.Episode)).ToArray(),
            Unit(metric), ChartPalette.Cyan, items);
    }
}
