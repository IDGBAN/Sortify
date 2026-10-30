using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Sortify.Models;
using Sortify.Services;
using Sortify.ViewModels;
using Sortify.Views;
using Xunit;
// Both namespaces define a DateTimePoint; the charts use LiveCharts' one.
using LcDateTimePoint = LiveChartsCore.Defaults.DateTimePoint;

namespace Sortify.Tests;

/// <summary>
/// A history where listening time and plays disagree: Long Band has three half-hour plays,
/// Quick Band six two-minute ones. By time Long Band leads; by plays Quick Band does.
/// </summary>
internal static class TwoWayHistory
{
    public static readonly DateTime Monday = new(2024, 1, 1, 20, 0, 0);
    public static readonly DateTime Tuesday = new(2024, 1, 2, 9, 0, 0);

    public static PlayRecord Play(string track, string artist, int minutes, DateTime when, string platform = "") => new()
    {
        TrackName = track,
        ArtistName = artist,
        AlbumName = artist + " LP",
        MsPlayed = minutes * 60_000,
        Timestamp = when,
        Platform = platform,
    };

    public static readonly PlayRecord[] Records =
    {
        Play("Epic", "Long Band", 30, Monday, "windows"),
        Play("Epic", "Long Band", 30, Monday.AddDays(7), "windows"),
        Play("Epic", "Long Band", 30, Monday.AddDays(35), "windows"),
        Play("Short", "Quick Band", 2, Tuesday, "android"),
        Play("Short", "Quick Band", 2, Tuesday.AddDays(1), "android"),
        Play("Short", "Quick Band", 2, Tuesday.AddDays(2), "android"),
        Play("Short", "Quick Band", 2, Tuesday.AddDays(7), "android"),
        Play("Short", "Quick Band", 2, Tuesday.AddDays(8), "android"),
        Play("Short", "Quick Band", 2, Tuesday.AddDays(35), "android"),
    };

    public static AnalysisResult Analyze() => AnalysisEngine.Analyze(Records, new FilterOptions());
}

/// <summary>Play counts the analysis keeps alongside listening time, for the Plays side of the switch.</summary>
public class MetricAnalysisTests
{
    [Fact]
    public void PlaysAreCountedByDayHourAndWeekday()
    {
        var r = TwoWayHistory.Analyze();

        Assert.Equal(3, r.PlaysByHour[20]);
        Assert.Equal(6, r.PlaysByHour[9]);
        Assert.Equal(3, r.PlaysByDayOfWeek[(int)DayOfWeek.Monday]);
        Assert.Equal(3, r.PlaysByDowHour[(int)DayOfWeek.Monday, 20]);
        Assert.Equal(r.TotalPlays, r.PlaysByDayOfWeek.Sum());

        // One point per listening day, on the same days as the listening-time series.
        Assert.Equal(r.PlaytimeByDay.Select(p => p.Date), r.PlaysByDay.Select(p => p.Date));
        Assert.Equal(r.TotalPlays, r.PlaysByDay.Sum(p => p.Value));
    }

    [Fact]
    public void MonthsAndYearsNameALeaderForEachMetric()
    {
        var r = TwoWayHistory.Analyze();

        var january = r.Months[0];
        Assert.Equal("Long Band", january.TopArtist);
        Assert.Equal("Quick Band", january.MostPlayedArtist);
        Assert.Equal(5, january.MostPlayedArtistPlays);
        Assert.Equal("Short", january.MostPlayedTrack);
        Assert.Equal("Quick Band", january.MostPlayedTrackArtist);
        Assert.Equal(5, january.MostPlayedTrackPlays);

        var year = Assert.Single(r.Years);
        Assert.Equal("Long Band", year.TopArtist);
        Assert.Equal("Epic", year.TopTrack);
        Assert.Equal("Quick Band", year.MostPlayedArtist);
        Assert.Equal("Short", year.MostPlayedTrack);
    }

    [Fact]
    public void ATieOnPlays_GoesToTheLongerListen()
    {
        var when = new DateTime(2024, 3, 1, 12, 0, 0);
        var r = AnalysisEngine.Analyze(new[]
        {
            TwoWayHistory.Play("Brief", "Brief Band", 1, when),
            TwoWayHistory.Play("Brief", "Brief Band", 1, when.AddHours(1)),
            TwoWayHistory.Play("Full", "Full Band", 5, when.AddHours(2)),
            TwoWayHistory.Play("Full", "Full Band", 5, when.AddHours(3)),
        }, new FilterOptions());

        Assert.Equal("Full Band", Assert.Single(r.Months).MostPlayedArtist);
    }

    [Fact]
    public void ShowsAndEpisodesComeRankedByPlaysToo()
    {
        PlayRecord Episode(string show, string episode, int minutes, int day) => new()
        {
            Kind = ContentKind.Podcast,
            ShowName = show,
            EpisodeName = episode,
            MsPlayed = minutes * 60_000,
            Timestamp = new DateTime(2024, 1, day, 8, 0, 0),
        };

        var r = AnalysisEngine.Analyze(new[]
        {
            Episode("Deep Dive", "Part 1", 90, 1),
            Episode("Daily", "Mon", 10, 2),
            Episode("Daily", "Tue", 10, 3),
            Episode("Daily", "Tue", 10, 4),
        }, new FilterOptions());

        Assert.Equal(new[] { "Deep Dive", "Daily" }, r.Shows.Select(s => s.Show));
        Assert.Equal(new[] { "Daily", "Deep Dive" }, r.ShowsByPlayCount.Select(s => s.Show));
        Assert.Equal("Tue", r.EpisodesByPlayCount[0].Episode);
        Assert.Equal("Part 1", r.Episodes[0].Episode);
    }

    [Fact]
    public void ADetailCountsPlaysByMonthAndHour()
    {
        var d = DetailEngine.Build(TwoWayHistory.Records, new FilterOptions(), DetailScope.Artist, "Quick Band", "");

        Assert.Equal(new[] { 5d, 1d }, d.PlaysByMonth.Select(p => p.Value));
        Assert.Equal(d.ByMonth.Select(p => p.Date), d.PlaysByMonth.Select(p => p.Date));
        Assert.Equal(6, d.PlaysByHour[9]);
        Assert.Equal(d.PlayCount, d.PlaysByHour.Sum());
    }

    [Fact]
    public void TheArtistTimelineCountsPlaysPerBucket()
    {
        var timeline = ArtistComparison.Build(TwoWayHistory.Records, new FilterOptions(),
            new[] { "Quick Band", "Long Band" }, ChartBuilder.TimeGranularity.Monthly);

        Assert.Equal(new[] { "Quick Band", "Long Band" }, timeline.PlaySeries.Select(s => s.Artist));
        Assert.Equal(new[] { 5d, 1d }, timeline.PlaySeries[0].Plays);
        Assert.Equal(new[] { 2d, 1d }, timeline.PlaySeries[1].Plays);
        Assert.Equal(new[] { 1d, 0.5 }, timeline.Series[1].Hours);
    }
}

/// <summary>Every chart drawn by plays rather than by time.</summary>
[Collection(WpfCollection.Name)]
public class MetricChartTests
{
    // The chart builder reaches the palette, whose brushes come from the WPF resources.
    private static T OnUiThread<T>(Func<T> build)
    {
        T value = default!;
        WpfTestHost.Run(() => value = build());
        return value;
    }

    private static double[] Values(ChartData chart) => chart.Series[0] switch
    {
        RowSeries<double> rows => rows.Values!.ToArray(),
        ColumnSeries<double> columns => columns.Values!.ToArray(),
        _ => throw new InvalidOperationException("Unexpected series " + chart.Series[0].GetType()),
    };

    private static string? AxisName(ChartData chart) => chart.YAxes.First().Name;

    [Fact]
    public void TopBars_RankAndMeasureByTheMetric()
    {
        var r = TwoWayHistory.Analyze();

        var byTime = OnUiThread(() => ChartBuilder.TopArtists(r, 2, Metric.Time));
        var byPlays = OnUiThread(() => ChartBuilder.TopArtists(r, 2, Metric.Plays));

        // Bars are drawn bottom-up, so the leader is last.
        Assert.Equal(new[] { "Quick Band", "Long Band" }, byTime.Items.Cast<ArtistStat>().Select(a => a.Artist));
        Assert.Equal(new[] { 0.2, 1.5 }, Values(byTime));
        Assert.Equal(new[] { "Long Band", "Quick Band" }, byPlays.Items.Cast<ArtistStat>().Select(a => a.Artist));
        Assert.Equal(new[] { 3d, 6d }, Values(byPlays));
        Assert.Equal("Plays", byPlays.Series[0].Name);
    }

    [Theory]
    [InlineData(Metric.Time)]
    [InlineData(Metric.Plays)]
    public void RankedBars_TakeTheUnitAndColourOfTheMetric(Metric metric)
    {
        var r = TwoWayHistory.Analyze();
        bool plays = metric == Metric.Plays;

        var charts = OnUiThread(() => new[]
        {
            (ChartBuilder.TopTracks(r, 5, metric), plays ? ChartPalette.Accent2 : ChartPalette.Accent),
            (ChartBuilder.TopArtists(r, 5, metric), plays ? ChartPalette.Accent2 : ChartPalette.Accent),
            (ChartBuilder.TopAlbums(r, 5, metric), plays ? ChartPalette.Accent2 : ChartPalette.Accent),
            // Shows and episodes keep a colour of their own either way.
            (ChartBuilder.TopShows(r, 5, metric), ChartPalette.Violet),
            (ChartBuilder.TopEpisodes(r, 5, metric), ChartPalette.Cyan),
        });

        foreach (var (chart, color) in charts)
        {
            var series = (RowSeries<double>)Assert.Single(chart.Series);
            Assert.Equal(plays ? "Plays" : "Hours", series.Name);
            Assert.Equal(color, ((SolidColorPaint)series.Fill!).Color);
        }
    }

    [Fact]
    public void ColumnCharts_CountPlays()
    {
        var r = TwoWayHistory.Analyze();

        var hours = OnUiThread(() => ChartBuilder.ByHour(r, Metric.Plays));
        Assert.Equal(6, Values(hours)[9]);
        Assert.Equal("Plays", AxisName(hours));

        var weekdays = OnUiThread(() => ChartBuilder.ByDayOfWeek(r, Metric.Plays));
        Assert.Equal(3, Values(weekdays)[(int)DayOfWeek.Monday]);

        var years = OnUiThread(() => ChartBuilder.PerYear(r, Metric.Plays));
        Assert.Equal(new[] { 9d }, Values(years));
        Assert.Equal("Hours", AxisName(OnUiThread(() => ChartBuilder.PerYear(r))));
    }

    [Fact]
    public void OverTime_SumsPlaysPerBucket()
    {
        var r = TwoWayHistory.Analyze();

        var chart = OnUiThread(() => ChartBuilder.OverTime(r, ChartBuilder.TimeGranularity.Monthly, Metric.Plays));

        var line = (LineSeries<LcDateTimePoint>)chart.Series[0];
        Assert.Equal("Plays/month", line.Name);
        Assert.Equal(new double?[] { 7, 2 }, line.Values!.Select(p => p.Value));
    }

    [Fact]
    public void TheHeatmap_WeighsSquaresByPlays()
    {
        var r = TwoWayHistory.Analyze();

        var chart = OnUiThread(() => ChartBuilder.DowHourHeat(r, Metric.Plays));

        var points = ((HeatSeries<WeightedPoint>)chart.Series[0]).Values!.ToList();
        // Points run day by day, 24 hours each.
        Assert.Equal(3d, points[(int)DayOfWeek.Monday * 24 + 20].Weight);
    }

    [Fact]
    public void Donuts_ShareOutPlays()
    {
        var r = TwoWayHistory.Analyze();

        var artists = OnUiThread(() => ChartBuilder.ArtistShare(r, Metric.Plays));
        Assert.Equal("Quick Band", artists[0].Name);
        Assert.Equal(66.7, OnUiThread(() => ChartBuilder.ArtistShareSlicesOf(r, Metric.Plays))[0].Percent, 1);
        Assert.Equal(new[] { 6d }, ((PieSeries<double>)artists[0]).Values);

        // By time the desktop leads; by plays the phone does.
        Assert.StartsWith("Desktop", OnUiThread(() => ChartBuilder.PlatformShare(r))[0].Name);
        Assert.Equal("Mobile", OnUiThread(() => ChartBuilder.PlatformShare(r, Metric.Plays))[0].Name);
        Assert.Equal(66.7, OnUiThread(() => ChartBuilder.PlatformSlicesOf(r, Metric.Plays))[0].Percent, 1);
    }

    [Fact]
    public void ATimelineAndADetail_DrawPlays()
    {
        var timeline = ArtistComparison.Build(TwoWayHistory.Records, new FilterOptions(),
            new[] { "Quick Band" }, ChartBuilder.TimeGranularity.Monthly);
        var line = (LineSeries<LcDateTimePoint>)OnUiThread(() =>
            ChartBuilder.ArtistTimelines(timeline, ChartBuilder.TimeGranularity.Monthly, Metric.Plays)).Series[0];
        Assert.Equal(new double?[] { 5, 1 }, line.Values!.Select(p => p.Value));

        var detail = DetailEngine.Build(TwoWayHistory.Records, new FilterOptions(), DetailScope.Artist, "Quick Band", "");
        var months = (ColumnSeries<LcDateTimePoint>)OnUiThread(() => ChartBuilder.DetailByMonth(detail, Metric.Plays)).Series[0];
        Assert.Equal(new double?[] { 5, 1 }, months.Values!.Select(p => p.Value));
        Assert.Equal(6, Values(OnUiThread(() => ChartBuilder.DetailByHour(detail, Metric.Plays)))[9]);
    }

    [Fact]
    public void PodcastBars_RankByPlays()
    {
        PlayRecord Episode(string show, int minutes, int day) => new()
        {
            Kind = ContentKind.Podcast,
            ShowName = show,
            EpisodeName = show + " " + day,
            MsPlayed = minutes * 60_000,
            Timestamp = new DateTime(2024, 1, day, 8, 0, 0),
        };
        var r = AnalysisEngine.Analyze(new[] { Episode("Long", 90, 1), Episode("Daily", 10, 2), Episode("Daily", 10, 3) },
            new FilterOptions());

        var chart = OnUiThread(() => ChartBuilder.TopShows(r, metric: Metric.Plays));

        Assert.Equal(new[] { "Long", "Daily" }, chart.Items.Cast<ShowStat>().Select(s => s.Show));
        Assert.Equal(new[] { 1d, 2d }, Values(chart));
    }
}

/// <summary>The Compare tab's tables ranked by plays.</summary>
public class MetricComparisonTests
{
    [Fact]
    public void RankingsAndTheTopArtistFollowTheMetric()
    {
        var a = AnalysisEngine.Analyze(TwoWayHistory.Records.Take(4).ToList(), new FilterOptions());
        var b = TwoWayHistory.Analyze();

        var byTime = PeriodComparison.Build(a, b);
        var byPlays = PeriodComparison.Build(a, b, Metric.Plays);

        Assert.Equal("Long Band", byTime.Artists[0].Name);
        Assert.Equal("Quick Band", byPlays.Artists[0].Name);
        Assert.Equal((1, 6), (byPlays.Artists[0].PlaysA, byPlays.Artists[0].PlaysB));
        Assert.Equal("Short", byPlays.Tracks[0].Name);
        Assert.Equal("Quick Band", byPlays.Metrics.Single(m => m.Metric == "Top artist").B);
    }
}

/// <summary>The Time / Plays switch, through the main view model.</summary>
[Collection(WpfCollection.Name)]
public class MetricSwitchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SortifyMetric_" + Guid.NewGuid().ToString("N"));

    public MetricSwitchTests()
    {
        Directory.CreateDirectory(_dir);
        RecordCache.Clear();
    }

    public void Dispose()
    {
        RecordCache.Clear();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>The two-way history as an extended-history export file.</summary>
    private string Export()
    {
        var path = Path.Combine(_dir, "Streaming_History_Audio_2024.json");
        File.WriteAllText(path, JsonSerializer.Serialize(TwoWayHistory.Records.Select(r => new
        {
            ts = r.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ms_played = r.MsPlayed,
            master_metadata_track_name = r.TrackName,
            master_metadata_album_artist_name = r.ArtistName,
            master_metadata_album_album_name = r.AlbumName,
            platform = r.Platform,
        })));
        return path;
    }

    [Fact]
    public async Task SwitchingToPlays_ReRanksTheListsAndCharts()
    {
        var settings = new AppSettings();
        string file = Export();

        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(settings);
            await vm.LoadFilesAsync(new[] { file });
            Assert.Equal("Epic", vm.Tracks[0].Track);
            Assert.Equal("Long Band", vm.TopArtists[0].Name);
            Assert.Equal(nameof(TrackStat.TotalMsPlayed), vm.RankSortMember);
            var years = vm.Years;

            vm.IsRankedByPlays = true;

            // The same years, as a new list, so a sort on a leader column that hid is dropped.
            Assert.NotSame(years, vm.Years);
            Assert.Equal(years, vm.Years);

            Assert.Equal(Metric.Plays, vm.Metric);
            Assert.False(vm.IsRankedByTime);
            Assert.Equal("Short", vm.Tracks[0].Track);
            Assert.Equal("Quick Band", vm.Artists[0].Artist);
            Assert.Equal("Quick Band LP", vm.Albums[0].Album);
            Assert.Equal(new RankedItem(1, "Quick Band", string.Empty, "6 plays", "12m 0s"), vm.TopArtists[0]);
            Assert.Equal("Quick Band", vm.Months[^1].TopArtist);
            Assert.Equal("5", vm.Months[^1].ArtistAmountText);
            Assert.Equal("Quick Band", ((ArtistStat)vm.ArtistsChart.Items[^1]).Artist);
            Assert.Equal("Plays", vm.ByHour.YAxes.First().Name);
            Assert.Equal(nameof(TrackStat.PlayCount), vm.RankSortMember);
            Assert.Equal("Plays", vm.AmountHeader);
            Assert.Equal(Metric.Plays, vm.Compare.Metric);
            Assert.Equal(Metric.Plays, settings.Metric);

            // The row filter keeps the plays order.
            vm.TrackQuickFilter = "o";
            Assert.Equal(new[] { "Short", "Epic" }, vm.Tracks.Select(t => t.Track));

            vm.IsRankedByTime = true;
            Assert.Equal(new[] { "Epic", "Short" }, vm.Tracks.Select(t => t.Track));
            Assert.Equal(Metric.Time, settings.Metric);
        });
    }

    [Fact]
    public async Task TheMetricIsRestoredAtStartup()
    {
        var settings = new AppSettings { Metric = Metric.Plays };
        string file = Export();

        await WpfTestHost.RunAsync(async () =>
        {
            var vm = new MainViewModel(settings);
            Assert.True(vm.IsRankedByPlays);

            await vm.LoadFilesAsync(new[] { file });

            Assert.Equal("Short", vm.Tracks[0].Track);
            Assert.Equal("Top 25 Artists - Share of Plays", vm.ArtistShareTitle);
        });
    }

    [Fact]
    public void TheSwitchSitsInTheFilterSidebar()
    {
        static IEnumerable<object> Logical(object node)
        {
            yield return node;
            if (node is DependencyObject d)
            {
                foreach (var child in LogicalTreeHelper.GetChildren(d))
                {
                    foreach (var nested in Logical(child))
                        yield return nested;
                }
            }
        }

        WpfTestHost.Run(() =>
        {
            var window = new MainWindow();
            var panel = Logical(window).OfType<FilterPanel>().Single();
            var halves = Logical(panel).OfType<RadioButton>().Where(r => r.GroupName == "Metric").ToList();
            Assert.Equal(new object[] { "Time", "Plays" }, halves.Select(r => r.Content));

            // The panel's own DataContext is the filters; the switch reaches past it to the window's,
            // which it finds once laid out.
            panel.Measure(new Size(290, 900));
            panel.Arrange(new Rect(0, 0, 290, 900));
            panel.UpdateLayout();
            ((MainViewModel)window.DataContext).Metric = Metric.Plays;
            Assert.Equal(new bool?[] { false, true }, halves.Select(r => r.IsChecked));

            halves[0].IsChecked = true;
            Assert.Equal(Metric.Time, ((MainViewModel)window.DataContext).Metric);
        });
    }

    [Fact]
    public void UncheckingOneHalfOfTheSwitch_ChangesNothing()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new MainViewModel(new AppSettings());

            vm.IsRankedByPlays = false;

            Assert.Equal(Metric.Time, vm.Metric);
        });
    }
}

/// <summary>Rows and wording that follow the metric.</summary>
public class MetricRowTests
{
    [Fact]
    public void ARankedRow_LeadsWithWhatItWasRankedBy()
    {
        using var culture = CultureScope.Named("en-US");
        var time = TimeSpan.FromMinutes(90);

        Assert.Equal(new RankedItem(1, "A", "B", "1h 30m", "1 play"), RankedItem.For(1, "A", "B", time, 1, Metric.Time));
        Assert.Equal(new RankedItem(2, "A", "B", "1,234 plays", "1h 30m"), RankedItem.For(2, "A", "B", time, 1234, Metric.Plays));
    }

    [Fact]
    public void AMonthRow_TakesTheLeadersOfItsMetric()
    {
        using var culture = CultureScope.Named("en-US");
        var month = TwoWayHistory.Analyze().Months[0];

        var byTime = MonthRow.For(month, Metric.Time);
        var byPlays = MonthRow.For(month, Metric.Plays);

        Assert.Equal(("Long Band", "Epic", "1.0"), (byTime.TopArtist, byTime.TopTrack, byTime.ArtistAmountText));
        Assert.Equal(month.TotalMsPlayed, byTime.Total);
        Assert.Equal(("Quick Band", "Short", "5"), (byPlays.TopArtist, byPlays.TopTrack, byPlays.ArtistAmountText));
        Assert.Equal("7 plays", byPlays.TotalText);
    }

    [Fact]
    public void TheMonthlyLeader_IsCountedByTheMetric()
    {
        var months = TwoWayHistory.Analyze().Months;

        Assert.StartsWith("Long Band was your #1 artist in 2 of 2 months", InsightText.MonthlyLeader(months));
        // February is a tie on plays (one each), which goes to the longer listen.
        Assert.StartsWith("Quick Band was your #1 artist in 1 of 2 months",
            InsightText.MonthlyLeader(months, Metric.Plays));
    }

    [Fact]
    public void TheMetric_IsSavedByName()
    {
        var json = JsonSerializer.Serialize(new AppSettings { Metric = Metric.Plays });

        Assert.Contains("\"Metric\":\"Plays\"", json);
        Assert.Equal(Metric.Plays, JsonSerializer.Deserialize<AppSettings>(json)!.Metric);
    }

    [Fact]
    public void AnUnknownMetric_FallsBackToTime()
    {
        var settings = new AppSettings { Metric = (Metric)7 };

        settings.Normalize();

        Assert.Equal(Metric.Time, settings.Metric);
    }
}
