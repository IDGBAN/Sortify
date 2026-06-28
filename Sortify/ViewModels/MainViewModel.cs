using System.Collections.ObjectModel;
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

    public FilterViewModel Filters { get; } = new();

    public ObservableCollection<TrackStat> Tracks { get; } = new();
    public ObservableCollection<ArtistStat> Artists { get; } = new();

    [ObservableProperty] private string _statusText = "Click \"Run Analysis\" and pick your Spotify history JSON files to begin.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasData;

    // Summary (overview) ------------------------------------------------------------------
    [ObservableProperty] private string _totalTimeText = "-";
    [ObservableProperty] private string _totalPlaysText = "-";
    [ObservableProperty] private string _uniqueArtistsText = "-";
    [ObservableProperty] private string _uniqueTracksText = "-";
    [ObservableProperty] private string _dateRangeText = "-";

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

    [ObservableProperty] private ISeries[] _hourSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _hourX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _hourY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _dayOfWeekSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _dayOfWeekX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _dayOfWeekY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _overTimeSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _overTimeX = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _overTimeY = Array.Empty<Axis>();

    [ObservableProperty] private ISeries[] _artistShareSeries = Array.Empty<ISeries>();

    // Heights that drive the scrollable horizontal bar charts (one per ~bar).
    [ObservableProperty] private double _tracksChartHeight = 480;
    [ObservableProperty] private double _artistsChartHeight = 480;

    // Infinite-scroll paging for the horizontal bar charts: start with one page and
    // append more bars as the user scrolls toward the bottom of a chart.
    private const int BarPageSize = 60;
    private int _tracksShown;
    private int _artistsShown;

    private int MaxTracks => Math.Min(ChartBuilder.MaxBars, _result.Tracks.Count);
    private int MaxArtists => Math.Min(ChartBuilder.MaxBars, _result.Artists.Count);

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

        IsBusy = true;
        try
        {
            var progress = new Progress<string>(s => StatusText = s);
            var parsed = await _parser.ParseAsync(dialog.FileNames, progress);
            _rawRecords = parsed.Records;

            if (_rawRecords.Count == 0)
            {
                HasData = false;
                StatusText = "No valid listening data found in the selected files.";
                return;
            }

            string warn = parsed.Warnings.Count > 0 ? $" ({parsed.Warnings.Count} file(s) skipped)" : string.Empty;
            StatusText = $"Loaded {_rawRecords.Count:N0} plays from {dialog.FileNames.Length} file(s){warn}. Crunching numbers...";
            HasData = true;
            await RecomputeAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RecomputeAsync()
    {
        if (!HasData) return;

        _analysisCts?.Cancel();
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
        UpdateCharts();

        ExportTxtCommand.NotifyCanExecuteChanged();
        ExportTracksCsvCommand.NotifyCanExecuteChanged();
        ExportArtistsCsvCommand.NotifyCanExecuteChanged();

        StatusText = _result.TotalPlays == 0
            ? "No plays match the current filters."
            : $"Showing {_result.TotalPlays:N0} plays across {_result.UniqueTracks:N0} tracks and {_result.UniqueArtists:N0} artists.";
    }

    private void UpdateCollections()
    {
        Tracks.Clear();
        foreach (var t in _result.Tracks) Tracks.Add(t);
        Artists.Clear();
        foreach (var a in _result.Artists) Artists.Add(a);
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

    private void UpdateCharts()
    {
        (HourSeries, HourX, HourY) = ChartBuilder.ByHour(_result);
        (DayOfWeekSeries, DayOfWeekX, DayOfWeekY) = ChartBuilder.ByDayOfWeek(_result);
        (OverTimeSeries, OverTimeX, OverTimeY) = ChartBuilder.OverTime(_result);
        ArtistShareSeries = ChartBuilder.ArtistShare(_result);

        // Reset the scrollable bar charts to their first page; LoadMore* append the rest.
        _tracksShown = Math.Min(BarPageSize, MaxTracks);
        _artistsShown = Math.Min(BarPageSize, MaxArtists);
        BuildTrackCharts();
        BuildArtistCharts();
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

    partial void OnHasDataChanged(bool value)
    {
        ExportTxtCommand.NotifyCanExecuteChanged();
        ExportTracksCsvCommand.NotifyCanExecuteChanged();
        ExportArtistsCsvCommand.NotifyCanExecuteChanged();
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
