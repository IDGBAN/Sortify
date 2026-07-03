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
    [ObservableProperty] private IReadOnlyList<SkippedTrackStat> _skippedTracks = Array.Empty<SkippedTrackStat>();

    [ObservableProperty] private string _statusText = "Click \"Run Analysis\" or drop your Spotify history JSON files here to begin.";
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
    [ObservableProperty] private string _biggestDayText = "-";
    [ObservableProperty] private string _activeDaysText = "-";
    [ObservableProperty] private string _avgPerDayText = "-";
    [ObservableProperty] private string _skipRateText = "-";
    [ObservableProperty] private string _peakHourText = "-";

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

    [ObservableProperty] private ISeries[] _artistShareSeries = Array.Empty<ISeries>();

    // Heights that drive the scrollable horizontal bar charts (one per ~bar).
    [ObservableProperty] private double _tracksChartHeight = 480;
    [ObservableProperty] private double _artistsChartHeight = 480;
    [ObservableProperty] private double _albumsChartHeight = 480;

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

    /// <summary>Parses the given history files and runs analysis. Also used by drag &amp; drop.</summary>
    public async Task LoadFilesAsync(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0 || IsBusy)
            return;

        IsBusy = true;
        try
        {
            var progress = new Progress<string>(s => StatusText = s);
            var parsed = await _parser.ParseAsync(filePaths, progress);
            _rawRecords = parsed.Records;

            if (_rawRecords.Count == 0)
            {
                HasData = false;
                StatusText = "No valid listening data found in the selected files.";
                return;
            }

            int problems = parsed.Warnings.Count + parsed.SkippedFiles.Count;
            string warn = problems > 0 ? $" ({problems} file(s) skipped)" : string.Empty;
            StatusText = $"Loaded {_rawRecords.Count:N0} plays from {filePaths.Count} file(s){warn}. Crunching numbers...";
            HasData = true;
            await RecomputeAsync();
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

    private async Task RecomputeAsync()
    {
        if (!HasData) return;

        _analysisCts?.Cancel();
        _analysisCts?.Dispose();
        _analysisCts = new CancellationTokenSource();
        var token = _analysisCts.Token;

        var options = Filters.ToOptions();
        try
        {
            _result = await AnalysisEngine.AnalyzeAsync(_rawRecords, options, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested) return;

        UpdateCollections();
        UpdateSummary();
        UpdateInsights();
        UpdateCharts();

        ExportTxtCommand.NotifyCanExecuteChanged();
        ExportTracksCsvCommand.NotifyCanExecuteChanged();
        ExportArtistsCsvCommand.NotifyCanExecuteChanged();
        ExportAlbumsCsvCommand.NotifyCanExecuteChanged();

        StatusText = _result.TotalPlays == 0
            ? "No plays match the current filters."
            : $"Showing {_result.TotalPlays:N0} plays across {_result.UniqueTracks:N0} tracks, " +
              $"{_result.UniqueArtists:N0} artists and {_result.UniqueAlbums:N0} albums.";
    }

    private void UpdateCollections()
    {
        Tracks = _result.Tracks;
        Artists = _result.Artists;
        Albums = _result.Albums;
        SkippedTracks = _result.SkippedTracks;
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

    private void UpdateCharts()
    {
        (HourSeries, HourX, HourY) = ChartBuilder.ByHour(_result);
        (DayOfWeekSeries, DayOfWeekX, DayOfWeekY) = ChartBuilder.ByDayOfWeek(_result);
        (HeatSeries, HeatX, HeatY) = ChartBuilder.DowHourHeat(_result);
        (OverTimeSeries, OverTimeX, OverTimeY) = ChartBuilder.OverTime(_result, _overTimeGranularity);
        (SkippedSeries, SkippedX, SkippedY) = ChartBuilder.TopSkippedTracks(_result);
        ArtistShareSeries = ChartBuilder.ArtistShare(_result);

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

    partial void OnHasDataChanged(bool value)
    {
        ExportTxtCommand.NotifyCanExecuteChanged();
        ExportTracksCsvCommand.NotifyCanExecuteChanged();
        ExportArtistsCsvCommand.NotifyCanExecuteChanged();
        ExportAlbumsCsvCommand.NotifyCanExecuteChanged();
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
