using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Win32;
using Sortify.Models;
using Sortify.Services;

namespace Sortify.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly HistoryParser _parser = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly DispatcherTimer _debounce;
    private List<PlayRecord> _rawRecords = new();
    private AnalysisResult _result = AnalysisResult.Empty;
    private CancellationTokenSource? _analysisCts;
    private ChartBuilder.TimeGranularity _overTimeGranularity = ChartBuilder.TimeGranularity.Daily;

    public FilterViewModel Filters { get; } = new();

    // Grid item sources. Swapped wholesale after each analysis pass instead of using
    // ObservableCollections: repopulating tens of thousands of rows item-by-item raises
    // a CollectionChanged per row and freezes the UI.
    [ObservableProperty] private IReadOnlyList<TrackStat> _tracks = Array.Empty<TrackStat>();
    [ObservableProperty] private IReadOnlyList<ArtistStat> _artists = Array.Empty<ArtistStat>();
    [ObservableProperty] private IReadOnlyList<AlbumStat> _albums = Array.Empty<AlbumStat>();
    [ObservableProperty] private IReadOnlyList<YearStat> _years = Array.Empty<YearStat>();
    [ObservableProperty] private IReadOnlyList<SkippedTrackStat> _skippedTracks = Array.Empty<SkippedTrackStat>();
    [ObservableProperty] private IReadOnlyList<ShowStat> _shows = Array.Empty<ShowStat>();
    [ObservableProperty] private IReadOnlyList<EpisodeStat> _episodes = Array.Empty<EpisodeStat>();

    [ObservableProperty] private string _statusText = "Click \"Run Analysis\" or drop your Spotify history JSON files (or their folder) here to begin.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasData;

    // Summary (overview) ------------------------------------------------------------------
    [ObservableProperty] private string _totalTimeText = "-";
    [ObservableProperty] private string _totalPlaysText = "-";
    [ObservableProperty] private string _uniqueArtistsText = "-";
    [ObservableProperty] private string _uniqueTracksText = "-";
    [ObservableProperty] private string _dateRangeText = "-";

    // Insights ------------------------------------------------------------------------------
    [ObservableProperty] private string _longestStreakText = "-";
    [ObservableProperty] private string _currentStreakText = "-";
    [ObservableProperty] private string _biggestDayText = "-";
    [ObservableProperty] private string _activeDaysText = "-";
    [ObservableProperty] private string _avgPerDayText = "-";
    [ObservableProperty] private string _skipRateText = "-";
    [ObservableProperty] private string _peakHourText = "-";
    [ObservableProperty] private string _sessionsText = "-";
    [ObservableProperty] private string _avgSessionText = "-";
    [ObservableProperty] private string _longestSessionText = "-";
    [ObservableProperty] private string _weekSplitText = "-";
    [ObservableProperty] private string _timeOfDayText = "-";

    // Playback context ---------------------------------------------------------------------
    [ObservableProperty] private string _shuffleRateText = "-";
    [ObservableProperty] private string _offlineRateText = "-";
    [ObservableProperty] private string _topDeviceText = "-";

    // Podcasts -----------------------------------------------------------------------------
    [ObservableProperty] private string _podcastTimeText = "-";
    [ObservableProperty] private string _podcastPlaysText = "-";
    [ObservableProperty] private string _uniqueShowsText = "-";
    [ObservableProperty] private string _uniqueEpisodesText = "-";
    [ObservableProperty] private bool _hasPodcastData;

    /// <summary>Drives the Podcasts tab's empty state; WPF ships no inverting bool converter.</summary>
    [ObservableProperty] private bool _hasNoPodcastData = true;

    // Charts ------------------------------------------------------------------------------
    [ObservableProperty] private ISeries[] _tracksByTimeSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _tracksByTimeX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _tracksByTimeY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _tracksByCountSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _tracksByCountX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _tracksByCountY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _artistsByTimeSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _artistsByTimeX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _artistsByTimeY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _artistsByCountSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _artistsByCountX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _artistsByCountY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _albumsByTimeSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _albumsByTimeX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _albumsByTimeY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _albumsByCountSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _albumsByCountX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _albumsByCountY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _skippedSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _skippedX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _skippedY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _hourSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _hourX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _hourY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _dayOfWeekSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _dayOfWeekX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _dayOfWeekY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _heatSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _heatX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _heatY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _overTimeSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _overTimeX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _overTimeY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _yearSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _yearX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _yearY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _discoverySeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _discoveryX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _discoveryY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _artistShareSeries = Array.Empty<ISeries>();
    [ObservableProperty] private ISeries[] _reasonEndSeries = Array.Empty<ISeries>();
    [ObservableProperty] private ISeries[] _platformSeries = Array.Empty<ISeries>();
    [ObservableProperty] private ISeries[] _countrySeries = Array.Empty<ISeries>();

    [ObservableProperty] private ISeries[] _showSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _showX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _showY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _episodeSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _episodeX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _episodeY = Array.Empty<Axis>();

    // Heights that drive the scrollable horizontal bar charts (one per ~bar).
    [ObservableProperty] private double _tracksChartHeight = 480;
    [ObservableProperty] private double _artistsChartHeight = 480;
    [ObservableProperty] private double _albumsChartHeight = 480;
    [ObservableProperty] private double _showsChartHeight = 220;
    [ObservableProperty] private double _episodesChartHeight = 220;

    // Infinite-scroll paging for the horizontal bar charts: start with one page and
    // append more bars as the user scrolls toward the bottom of a chart.
    private const int BarPageSize = 60;
    private int _tracksShown;
    private int _artistsShown;
    private int _albumsShown;

    private int MaxTracks => Math.Min(ChartBuilder.MaxBars, _result.Tracks.Count);
    private int MaxArtists => Math.Min(ChartBuilder.MaxBars, _result.Artists.Count);
    private int MaxAlbums => Math.Min(ChartBuilder.MaxBars, _result.Albums.Count);

    public MainViewModel()
    {
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await RecomputeAsync();
        };
        Filters.FiltersChanged += (_, _) =>
        {
            if (!HasData) return;
            _debounce.Stop();
            _debounce.Start();
        };
    }

    [RelayCommand]
    private async Task RunAnalysisAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select your Spotify streaming history JSON files",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true)
            return;

        await LoadFilesAsync(dialog.FileNames);
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the folder that contains your Spotify streaming history",
        };
        if (dialog.ShowDialog() != true)
            return;

        var files = HistoryParser.FindHistoryFiles(dialog.FolderName);
        if (files.Count == 0)
        {
            StatusText = "No Spotify history JSON files were found in that folder.";
            return;
        }

        _settings.LastFolder = dialog.FolderName;
        _settings.Save();
        await LoadFilesAsync(files);
    }

    /// <summary>
    /// Reopens the folder used last session. Called once at startup; silently does nothing
    /// when there is no remembered folder, it has gone away, or the user turned this off.
    /// </summary>
    public async Task RestoreLastFolderAsync()
    {
        if (!_settings.ReopenLastFolder || string.IsNullOrWhiteSpace(_settings.LastFolder))
            return;

        var files = HistoryParser.FindHistoryFiles(_settings.LastFolder);
        if (files.Count == 0)
            return;

        await LoadFilesAsync(files);
    }

    /// <summary>Parses the given history files and runs analysis. Also used by drag &amp; drop.</summary>
    public async Task LoadFilesAsync(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0 || IsBusy)
            return;

        IsBusy = true;
        try
        {
            // Re-reading an unchanged export is the common case (relaunching the app, or
            // reopening the same folder), and parsing it again costs seconds for nothing.
            StatusText = "Checking for cached results...";
            var cached = await Task.Run(() => RecordCache.TryLoad(filePaths));
            if (cached is { Count: > 0 })
            {
                _rawRecords = cached.ToList();
                StatusText = $"Loaded {_rawRecords.Count:N0} plays from cache. Crunching numbers...";
                HasData = true;
                await RecomputeAsync();
                return;
            }

            var progress = new Progress<string>(s => StatusText = s);
            var parsed = await _parser.ParseAsync(filePaths, progress);
            _rawRecords = parsed.Records;

            if (_rawRecords.Count == 0)
            {
                HasData = false;
                StatusText = parsed.Warnings.Count > 0
                    ? $"No valid listening data found. {parsed.Warnings[0]}"
                    : "No valid listening data found in the selected files.";
                return;
            }

            int problems = parsed.Warnings.Count + parsed.SkippedFiles.Count;
            string warn = problems > 0 ? $" ({problems} file(s) skipped)" : string.Empty;
            StatusText = $"Loaded {_rawRecords.Count:N0} plays from {filePaths.Count} file(s){warn}. Crunching numbers...";
            HasData = true;
            await RecomputeAsync();

            // Save after analysis so the user isn't waiting on disk I/O to see results.
            var toCache = _rawRecords;
            _ = Task.Run(() => RecordCache.TrySave(filePaths, toCache));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Changes the bucket size of the listening-over-time chart.</summary>
    public void SetOverTimeGranularity(ChartBuilder.TimeGranularity granularity)
    {
        if (_overTimeGranularity == granularity)
            return;
        _overTimeGranularity = granularity;
        if (HasData)
            (OverTimeSeries, OverTimeX, OverTimeY) = ChartBuilder.OverTime(_result, granularity);
    }

    // ---- Grid-driven exclusions ----------------------------------------------------------

    public void ExcludeArtistFromGrid(string artist) => Filters.ExcludeArtist(artist);
    public void ExcludeTrackFromGrid(string track) => Filters.ExcludeTrack(track);

    // ---- Drill-down ------------------------------------------------------------------------

    /// <summary>
    /// Builds the focused breakdown for one grid row, under the filters currently in effect.
    /// Returns null when there is nothing loaded to drill into.
    /// </summary>
    public async Task<DetailResult?> BuildDetailAsync(DetailScope scope, string title, string subtitle)
    {
        if (!HasData || _rawRecords.Count == 0)
            return null;

        return await DetailEngine.BuildAsync(_rawRecords, Filters.ToOptions(), scope, title, subtitle);
    }

    private async Task RecomputeAsync()
    {
        if (!HasData) return;

        _analysisCts?.Cancel();
        _analysisCts?.Dispose();
        _analysisCts = new CancellationTokenSource();
        var token = _analysisCts.Token;

        var options = Filters.ToOptions();
        bool wasBusy = IsBusy;
        IsBusy = true;
        try
        {
            AnalysisResult result;
            try
            {
                result = await AnalysisEngine.AnalyzeAsync(_rawRecords, options, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // A newer recompute may have started while this one ran; never let a stale
            // result overwrite the current one.
            if (token.IsCancellationRequested)
                return;
            _result = result;

            UpdateCollections();
            UpdateSummary();
            UpdateInsights();
            UpdateCharts();
            NotifyExportsChanged();

            StatusText = _result.TotalPlays == 0
                ? "No plays match the current filters."
                : $"Showing {_result.TotalPlays:N0} plays across {_result.UniqueTracks:N0} tracks, " +
                  $"{_result.UniqueArtists:N0} artists and {_result.UniqueAlbums:N0} albums.";
        }
        finally
        {
            IsBusy = wasBusy;
        }
    }

    private void UpdateCollections()
    {
        Tracks = _result.Tracks;
        Artists = _result.Artists;
        Albums = _result.Albums;
        Years = _result.Years;
        SkippedTracks = _result.SkippedTracks;
        Shows = _result.Shows;
        Episodes = _result.Episodes;
        HasPodcastData = _result.Shows.Count > 0;
        HasNoPodcastData = !HasPodcastData;
    }

    private void UpdateSummary()
    {
        TotalTimeText = TimeFormat.Friendly(_result.TotalTime);
        TotalPlaysText = _result.TotalPlays.ToString("N0");
        UniqueArtistsText = _result.UniqueArtists.ToString("N0");
        UniqueTracksText = _result.UniqueTracks.ToString("N0");
        DateRangeText = _result.FirstListen is { } f && _result.LastListen is { } l
            ? $"{TimeFormat.Timestamp(f)}  to  {TimeFormat.Timestamp(l)}"
            : "-";
    }

    private void UpdateInsights()
    {
        var r = _result;

        LongestStreakText = r.LongestStreakDays > 0 && r.LongestStreakStart is { } ss && r.LongestStreakEnd is { } se
            ? $"{r.LongestStreakDays} day{(r.LongestStreakDays == 1 ? "" : "s")}  ({ss:yyyy-MM-dd} to {se:yyyy-MM-dd})"
            : "-";

        CurrentStreakText = r.CurrentStreakDays > 0 && r.LastListen is { } lastListen
            ? $"{r.CurrentStreakDays} day{(r.CurrentStreakDays == 1 ? "" : "s")}  (up to {lastListen:yyyy-MM-dd})"
            : "-";

        BiggestDayText = r.BiggestDay is { } bd
            ? $"{bd:yyyy-MM-dd}  ({TimeFormat.Friendly(TimeSpan.FromMilliseconds(r.BiggestDayMs))})"
            : "-";

        ActiveDaysText = r.ActiveDays > 0 ? r.ActiveDays.ToString("N0") : "-";

        AvgPerDayText = r.ActiveDays > 0
            ? TimeFormat.Friendly(TimeSpan.FromMilliseconds((double)r.TotalMsPlayed / r.ActiveDays))
            : "-";

        SkipRateText = r.SkipEligiblePlays > 0
            ? $"{r.TotalSkips * 100.0 / r.SkipEligiblePlays:0.#}%  ({r.TotalSkips:N0} of {r.SkipEligiblePlays:N0} plays)"
            : "-";

        PeakHourText = BuildPeakHourText(r);

        SessionsText = r.SessionCount > 0 ? r.SessionCount.ToString("N0") : "-";

        AvgSessionText = r.SessionCount > 0
            ? TimeFormat.Friendly(TimeSpan.FromMilliseconds(r.AvgSessionMs))
            : "-";

        LongestSessionText = r.LongestSessionMs > 0 && r.LongestSessionDate is { } sd
            ? $"{TimeFormat.Friendly(TimeSpan.FromMilliseconds(r.LongestSessionMs))}  ({sd:yyyy-MM-dd})"
            : "-";

        WeekSplitText = BuildWeekSplitText(r);
        TimeOfDayText = BuildTimeOfDayText(r);

        // Playback context. Older exports omit these flags, hence the eligibility check.
        ShuffleRateText = r.ShuffleEligiblePlays > 0
            ? $"{r.ShufflePlays * 100.0 / r.ShuffleEligiblePlays:0.#}%  ({r.ShufflePlays:N0} of {r.ShuffleEligiblePlays:N0} plays)"
            : "-";

        OfflineRateText = r.ShuffleEligiblePlays > 0
            ? $"{r.OfflinePlays * 100.0 / r.ShuffleEligiblePlays:0.#}%  ({r.OfflinePlays:N0} plays)"
            : "-";

        TopDeviceText = r.Platforms.Count > 0
            ? $"{r.Platforms[0].Name}  ({TimeFormat.Friendly(r.Platforms[0].TotalTime)})"
            : "-";

        // Podcasts.
        PodcastTimeText = r.PodcastMsPlayed > 0
            ? TimeFormat.Friendly(TimeSpan.FromMilliseconds(r.PodcastMsPlayed))
            : "-";
        PodcastPlaysText = r.PodcastPlays > 0 ? r.PodcastPlays.ToString("N0") : "-";
        UniqueShowsText = r.Shows.Count > 0 ? r.Shows.Count.ToString("N0") : "-";
        UniqueEpisodesText = r.Episodes.Count > 0 ? r.Episodes.Count.ToString("N0") : "-";
    }

    private static string BuildPeakHourText(AnalysisResult r)
    {
        if (r.TotalMsPlayed == 0)
            return "-";

        int peakHour = 0;
        for (int h = 1; h < 24; h++)
            if (r.PlaytimeByHour[h] > r.PlaytimeByHour[peakHour]) peakHour = h;

        int peakDow = 0;
        for (int d = 1; d < 7; d++)
            if (r.PlaytimeByDayOfWeek[d] > r.PlaytimeByDayOfWeek[peakDow]) peakDow = d;

        string[] dayNames = { "Sundays", "Mondays", "Tuesdays", "Wednesdays", "Thursdays", "Fridays", "Saturdays" };
        return $"{peakHour:00}:00-{(peakHour + 1) % 24:00}:00 on {dayNames[peakDow]}";
    }

    private static string BuildWeekSplitText(AnalysisResult r)
    {
        long weekend = r.PlaytimeByDayOfWeek[0] + r.PlaytimeByDayOfWeek[6];
        long total = 0;
        foreach (var ms in r.PlaytimeByDayOfWeek) total += ms;
        if (total == 0)
            return "-";

        double weekendPct = weekend * 100.0 / total;
        return $"{100 - weekendPct:0}% weekdays  /  {weekendPct:0}% weekends";
    }

    private static string BuildTimeOfDayText(AnalysisResult r)
    {
        long total = 0;
        foreach (var ms in r.PlaytimeByHour) total += ms;
        if (total == 0)
            return "-";

        Span<long> segments = stackalloc long[4]; // night, morning, afternoon, evening
        for (int h = 0; h < 24; h++)
            segments[h / 6] += r.PlaytimeByHour[h];

        string[] names = { "Night (00-06)", "Morning (06-12)", "Afternoon (12-18)", "Evening (18-24)" };
        int best = 0;
        for (int i = 1; i < 4; i++)
            if (segments[i] > segments[best]) best = i;

        return $"{names[best]}  ({segments[best] * 100.0 / total:0}% of listening)";
    }

    private void UpdateCharts()
    {
        (HourSeries, HourX, HourY) = ChartBuilder.ByHour(_result);
        (DayOfWeekSeries, DayOfWeekX, DayOfWeekY) = ChartBuilder.ByDayOfWeek(_result);
        (HeatSeries, HeatX, HeatY) = ChartBuilder.DowHourHeat(_result);
        (OverTimeSeries, OverTimeX, OverTimeY) = ChartBuilder.OverTime(_result, _overTimeGranularity);
        (YearSeries, YearX, YearY) = ChartBuilder.HoursPerYear(_result);
        (DiscoverySeries, DiscoveryX, DiscoveryY) = ChartBuilder.NewArtistsByMonth(_result);
        (SkippedSeries, SkippedX, SkippedY) = ChartBuilder.TopSkippedTracks(_result);
        ArtistShareSeries = ChartBuilder.ArtistShare(_result);
        ReasonEndSeries = ChartBuilder.ReasonEndShare(_result);
        PlatformSeries = ChartBuilder.PlatformShare(_result);
        CountrySeries = ChartBuilder.CountryShare(_result);
        // Size these to their content: most libraries hold only a handful of shows, and a
        // fixed-height chart would space three bars across half a screen.
        const int podcastBars = 20;
        (ShowSeries, ShowX, ShowY) = ChartBuilder.TopShows(_result, podcastBars);
        (EpisodeSeries, EpisodeX, EpisodeY) = ChartBuilder.TopEpisodes(_result, podcastBars);
        ShowsChartHeight = BarHeight(Math.Min(podcastBars, _result.Shows.Count));
        EpisodesChartHeight = BarHeight(Math.Min(podcastBars, _result.Episodes.Count));

        // Reset the scrollable bar charts to their first page; LoadMore* append the rest.
        _tracksShown = Math.Min(BarPageSize, MaxTracks);
        _artistsShown = Math.Min(BarPageSize, MaxArtists);
        _albumsShown = Math.Min(BarPageSize, MaxAlbums);
        BuildTrackCharts();
        BuildArtistCharts();
        BuildAlbumCharts();
    }

    private static double BarHeight(int bars)
    {
        const double perBar = 34;
        const double axisPadding = 70;
        return Math.Max(220, bars * perBar + axisPadding);
    }

    private void BuildTrackCharts()
    {
        (TracksByTimeSeries, TracksByTimeX, TracksByTimeY) = ChartBuilder.TopTracksByTime(_result, _tracksShown);
        (TracksByCountSeries, TracksByCountX, TracksByCountY) = ChartBuilder.TopTracksByCount(_result, _tracksShown);
        TracksChartHeight = BarHeight(_tracksShown);
    }

    private void BuildArtistCharts()
    {
        (ArtistsByTimeSeries, ArtistsByTimeX, ArtistsByTimeY) = ChartBuilder.TopArtistsByTime(_result, _artistsShown);
        (ArtistsByCountSeries, ArtistsByCountX, ArtistsByCountY) = ChartBuilder.TopArtistsByCount(_result, _artistsShown);
        ArtistsChartHeight = BarHeight(_artistsShown);
    }

    private void BuildAlbumCharts()
    {
        (AlbumsByTimeSeries, AlbumsByTimeX, AlbumsByTimeY) = ChartBuilder.TopAlbumsByTime(_result, _albumsShown);
        (AlbumsByCountSeries, AlbumsByCountX, AlbumsByCountY) = ChartBuilder.TopAlbumsByCount(_result, _albumsShown);
        AlbumsChartHeight = BarHeight(_albumsShown);
    }

    /// <summary>Appends another page of track bars; called as the user scrolls down.</summary>
    public void LoadMoreTracks()
    {
        if (_tracksShown >= MaxTracks) return;
        _tracksShown = Math.Min(_tracksShown + BarPageSize, MaxTracks);
        BuildTrackCharts();
    }

    /// <summary>Appends another page of artist bars; called as the user scrolls down.</summary>
    public void LoadMoreArtists()
    {
        if (_artistsShown >= MaxArtists) return;
        _artistsShown = Math.Min(_artistsShown + BarPageSize, MaxArtists);
        BuildArtistCharts();
    }

    /// <summary>Appends another page of album bars; called as the user scrolls down.</summary>
    public void LoadMoreAlbums()
    {
        if (_albumsShown >= MaxAlbums) return;
        _albumsShown = Math.Min(_albumsShown + BarPageSize, MaxAlbums);
        BuildAlbumCharts();
    }

    private bool CanExport() => HasData && _result.TotalPlays > 0;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportTxtAsync()
    {
        var path = AskSave("Text files (*.txt)|*.txt", ".txt", "Sortify_results");
        if (path is null) return;
        await ExportService.SaveTxtAsync(path, _result);
        StatusText = $"Saved results to {path}";
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportTracksCsvAsync()
    {
        var path = AskSave("CSV files (*.csv)|*.csv", ".csv", "Sortify_tracks");
        if (path is null) return;
        await ExportService.SaveTracksCsvAsync(path, _result);
        StatusText = $"Saved tracks CSV to {path}";
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportArtistsCsvAsync()
    {
        var path = AskSave("CSV files (*.csv)|*.csv", ".csv", "Sortify_artists");
        if (path is null) return;
        await ExportService.SaveArtistsCsvAsync(path, _result);
        StatusText = $"Saved artists CSV to {path}";
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAlbumsCsvAsync()
    {
        var path = AskSave("CSV files (*.csv)|*.csv", ".csv", "Sortify_albums");
        if (path is null) return;
        await ExportService.SaveAlbumsCsvAsync(path, _result);
        StatusText = $"Saved albums CSV to {path}";
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportYearsCsvAsync()
    {
        var path = AskSave("CSV files (*.csv)|*.csv", ".csv", "Sortify_years");
        if (path is null) return;
        await ExportService.SaveYearsCsvAsync(path, _result);
        StatusText = $"Saved years CSV to {path}";
    }

    partial void OnHasDataChanged(bool value) => NotifyExportsChanged();

    private void NotifyExportsChanged()
    {
        ExportTxtCommand.NotifyCanExecuteChanged();
        ExportTracksCsvCommand.NotifyCanExecuteChanged();
        ExportArtistsCsvCommand.NotifyCanExecuteChanged();
        ExportAlbumsCsvCommand.NotifyCanExecuteChanged();
        ExportYearsCsvCommand.NotifyCanExecuteChanged();
    }

    private static string? AskSave(string filter, string ext, string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            DefaultExt = ext,
            FileName = defaultName + ext,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
