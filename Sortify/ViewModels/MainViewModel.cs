using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using Microsoft.Win32;
using Sortify.Models;
using Sortify.Services;

namespace Sortify.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>How the loaded export's play times were converted, so a change in the setting can read it again.</summary>
    private string? _loadedTimeZones;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _debounce;
    private List<PlayRecord> _rawRecords = new();

    /// <summary>The plays with re-releases folded together, worked out the first time the option is on.</summary>
    private List<PlayRecord>? _combinedRecords;

    /// <summary>
    /// The plays every view reads: as loaded, or with remasters and deluxe editions counted
    /// under the original when the sidebar asks. Going through one list keeps exclusions,
    /// searches and drill-downs matching the names on screen.
    /// </summary>
    private IReadOnlyList<PlayRecord> Records => Filters.CombineReleases
        ? _combinedRecords ??= ReleaseNames.Combine(_rawRecords)
        : _rawRecords;
    private AnalysisResult _result = AnalysisResult.Empty;

    /// <summary>The filters <see cref="_result"/> was computed under, as the chip row words them.</summary>
    private IReadOnlyList<string> _resultFilters = Array.Empty<string>();

    /// <summary>The filters <see cref="_result"/> was computed under, for views that list its plays.</summary>
    private FilterOptions? _resultOptions;

    private CancellationTokenSource? _analysisCts;
    private CancellationTokenSource? _loadCts;
    private ChartBuilder.TimeGranularity _overTimeGranularity = ChartBuilder.TimeGranularity.Daily;

    /// <summary>Files behind the current results, so F5 can re-read them.</summary>
    private IReadOnlyList<string> _loadedFiles = Array.Empty<string>();

    /// <summary>Folder or ZIP those files came from, or null when they were picked individually.</summary>
    private string? _loadedFolder;

    // IsBusy covers two independent things - a parse and any number of overlapping analysis
    // passes - so it is derived from both rather than saved and restored, which loses track
    // as soon as two recomputes overlap.
    private bool _isLoading;
    private int _pendingAnalyses;

    public FilterViewModel Filters { get; } = new();

    [ObservableProperty] private IReadOnlyList<SearchSuggestion> _suggestions = Array.Empty<SearchSuggestion>();
    [ObservableProperty] private IReadOnlyList<SearchSuggestion> _tableSuggestions = Array.Empty<SearchSuggestion>();

    /// <summary>The Compare Periods tab.</summary>
    public CompareViewModel Compare { get; }

    /// <summary>The Compare People tab.</summary>
    public PeopleCompareViewModel ComparePeople { get; }

    /// <summary>The History tab.</summary>
    public HistoryViewModel History { get; }

    /// <summary>The listening calendar on the Trends tab.</summary>
    public CalendarViewModel Calendar { get; } = new();

    /// <summary>Compare Artists on the Trends tab.</summary>
    public CompareArtistsViewModel CompareArtists { get; }

    /// <summary>The day with the most listening in the current results, for the Insights card's link.</summary>
    public DateTime? BiggestDay => _result.BiggestDay;

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
    /// <summary>Each month's top artist and track by the chosen metric, newest month first.</summary>
    [ObservableProperty] private IReadOnlyList<MonthRow> _months = Array.Empty<MonthRow>();
    [ObservableProperty] private string _monthlyLeaderText = string.Empty;
    [ObservableProperty] private IReadOnlyList<Milestone> _milestones = Array.Empty<Milestone>();
    [ObservableProperty] private IReadOnlyList<RepeatStat> _onRepeat = Array.Empty<RepeatStat>();

    /// <summary>What puts a track on the On Repeat list, worded for the Insights card.</summary>
    public string OnRepeatHint { get; } =
        $"The tracks you played at least {AnalysisEngine.RepeatMinPlays} times within {AnalysisEngine.RepeatWindowDays} days, " +
        "each at its busiest stretch. Double-click one for its breakdown.";

    public string OnRepeatEmpty { get; } =
        $"No track was played {AnalysisEngine.RepeatMinPlays} times within {AnalysisEngine.RepeatWindowDays} days under the current filters.";
    [ObservableProperty] private IReadOnlyList<TrackStat> _forgottenTracks = Array.Empty<TrackStat>();
    [ObservableProperty] private IReadOnlyList<ArtistStat> _forgottenArtists = Array.Empty<ArtistStat>();

    /// <summary>The rule a forgotten favorite has to meet, worded for the Insights card.</summary>
    public string ForgottenHint { get; } =
        $"Tracks you played at least {AnalysisEngine.ForgottenMinTrackPlays} times and artists at least " +
        $"{AnalysisEngine.ForgottenMinArtistPlays} times, with nothing in the last {AnalysisEngine.ForgottenAfterDays} " +
        "days of the history shown. Double-click one for its breakdown.";

    // Top-five lists shown on the Overview tab.
    [ObservableProperty] private IReadOnlyList<RankedItem> _topTracks = Array.Empty<RankedItem>();
    [ObservableProperty] private IReadOnlyList<RankedItem> _topArtists = Array.Empty<RankedItem>();
    [ObservableProperty] private IReadOnlyList<RankedItem> _topAlbums = Array.Empty<RankedItem>();

    // Per-grid quick filters. These narrow the rows already on screen without re-running
    // analysis, which is what you want when hunting for one row in fifty thousand.
    [ObservableProperty] private string _trackQuickFilter = string.Empty;
    [ObservableProperty] private string _artistQuickFilter = string.Empty;
    [ObservableProperty] private string _albumQuickFilter = string.Empty;
    [ObservableProperty] private string _showQuickFilter = string.Empty;

    [ObservableProperty]
    private string _statusText = "Open your Spotify history to begin - or just drop the files here.";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasData;

    /// <summary>True while the made-up sample history is open rather than a real export.</summary>
    [ObservableProperty] private bool _isSample;

    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isProgressIndeterminate = true;

    /// <summary>Filter sidebar visibility, restored from and saved to settings.</summary>
    [ObservableProperty] private bool _sidebarVisible = true;

    /// <summary>The filters currently narrowing the results, each removable on its own.</summary>
    public ObservableCollection<FilterChip> ActiveFilters { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFilterBar))]
    private bool _hasActiveFilters;

    /// <summary>
    /// The row above the tabs shows the active filters, and stays up after they are all cleared
    /// while there is a change to undo, so a stray "Clear all" can be taken back.
    /// </summary>
    public bool ShowFilterBar => HasActiveFilters || Filters.CanUndo || Filters.CanRedo;

    /// <summary>Folders opened previously, newest first; bound to the Open Recent menu.</summary>
    public ObservableCollection<string> RecentFolders { get; } = new();

    [ObservableProperty] private string _themeGlyph = "";
    [ObservableProperty] private string _themeTooltip = "Switch to the light theme";

    /// <summary>
    /// What every chart and list ranks and measures by: the Time / Plays switch in the sidebar.
    /// The analysis already holds both, so switching only re-ranks and redraws.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRankedByTime), nameof(IsRankedByPlays), nameof(RankSortMember),
        nameof(RankedByText), nameof(ArtistShareTitle), nameof(PerYearTitle), nameof(MonthlyTopHint),
        nameof(AmountHeader), nameof(RankHint))]
    private Metric _metric;

    /// <summary>The Time half of the switch. Unchecking it does nothing; checking Plays does.</summary>
    public bool IsRankedByTime
    {
        get => Metric == Metric.Time;
        set
        {
            if (value)
                Metric = Metric.Time;
        }
    }

    /// <summary>The Plays half of the switch.</summary>
    public bool IsRankedByPlays
    {
        get => Metric == Metric.Plays;
        set
        {
            if (value)
                Metric = Metric.Plays;
        }
    }

    /// <summary>The column the ranked grids arrive sorted by, so that header can show the arrow.</summary>
    public string RankSortMember => Metric == Metric.Plays ? nameof(TrackStat.PlayCount) : nameof(TrackStat.TotalMsPlayed);

    /// <summary>Follows a ranked chart's title: "by listening time" or "by plays".</summary>
    public string RankedByText => Metric == Metric.Plays ? "by plays" : "by listening time";

    public string ArtistShareTitle => Metric == Metric.Plays
        ? $"Top {ChartBuilder.ArtistShareSlices} Artists - Share of Plays"
        : $"Top {ChartBuilder.ArtistShareSlices} Artists - Share of Listening Time";

    public string PerYearTitle => Metric == Metric.Plays ? "Plays per Year" : "Listening Time per Year";

    public string MonthlyTopHint => (Metric == Metric.Plays
        ? "The artist and track you played most each month."
        : "The artist and track you listened to longest each month.") +
        " Double-click a month for its top artist; right-click for its top track.";

    /// <summary>Header for a column holding a listening amount: "Hours" or "Plays".</summary>
    public string AmountHeader => Metric == Metric.Plays ? "Plays" : "Hours";

    // Summary (overview) ------------------------------------------------------------------
    [ObservableProperty] private string _totalTimeText = "-";
    [ObservableProperty] private string _totalPlaysText = "-";
    [ObservableProperty] private string _uniqueArtistsText = "-";
    [ObservableProperty] private string _uniqueTracksText = "-";
    [ObservableProperty] private string _dateRangeText = "-";

    /// <summary>What the history on screen was read from, for the panel the status bar opens.</summary>
    [ObservableProperty] private LoadedHistory? _loadedHistory;

    /// <summary>The status bar's link to that panel: "12 files loaded, 1 skipped".</summary>
    [ObservableProperty] private string _loadedFilesText = string.Empty;

    /// <summary>The Overview's card button, naming what the card would cover: "Summary card for March 2024".</summary>
    [ObservableProperty] private string _summaryCardText = "Summary card";

    /// <summary>The Insights and Podcasts tabs' figures, in words.</summary>
    [ObservableProperty] private InsightText _insights = InsightText.Empty;

    [ObservableProperty] private bool _hasPodcastData;

    // Charts ------------------------------------------------------------------------------
    [ObservableProperty] private ChartData _tracksChart = ChartData.Empty;
    [ObservableProperty] private ChartData _artistsChart = ChartData.Empty;
    [ObservableProperty] private ChartData _albumsChart = ChartData.Empty;
    [ObservableProperty] private ChartData _skipped = ChartData.Empty;
    [ObservableProperty] private ChartData _byHour = ChartData.Empty;
    [ObservableProperty] private ChartData _byDayOfWeek = ChartData.Empty;
    [ObservableProperty] private ChartData _heat = ChartData.Empty;
    [ObservableProperty] private ChartData _overTime = ChartData.Empty;
    [ObservableProperty] private ChartData _byYear = ChartData.Empty;
    [ObservableProperty] private ChartData _rankChart = ChartData.Empty;
    [ObservableProperty] private bool _hasRankHistory;
    [ObservableProperty] private ChartData _discovery = ChartData.Empty;
    [ObservableProperty] private ChartData _showsChart = ChartData.Empty;
    [ObservableProperty] private ChartData _episodesChart = ChartData.Empty;

    /// <summary>What the rank history follows, worded for its card.</summary>
    public string RankHint =>
        $"Where your top {AnalysisEngine.RankHistoryArtists} artists ranked each year {RankedByText}. " +
        $"A gap means they were outside the top {AnalysisEngine.RankHistoryFloor} that year.";

    [ObservableProperty] private ISeries[] _artistShareSeries = Array.Empty<ISeries>();
    [ObservableProperty] private IReadOnlyList<LegendEntry> _artistShareLegend = Array.Empty<LegendEntry>();
    [ObservableProperty] private IReadOnlyList<LegendEntry> _reasonEndLegend = Array.Empty<LegendEntry>();
    [ObservableProperty] private IReadOnlyList<LegendEntry> _platformLegend = Array.Empty<LegendEntry>();
    [ObservableProperty] private IReadOnlyList<LegendEntry> _countryLegend = Array.Empty<LegendEntry>();
    [ObservableProperty] private ISeries[] _reasonEndSeries = Array.Empty<ISeries>();
    [ObservableProperty] private ISeries[] _platformSeries = Array.Empty<ISeries>();
    [ObservableProperty] private ISeries[] _countrySeries = Array.Empty<ISeries>();

    /// <summary>Entry animation length for charts when animations are on.</summary>
    private static readonly TimeSpan ChartAnimationDuration = TimeSpan.FromMilliseconds(500);

    /// <summary>How long filter edits settle before analysis re-runs.</summary>
    private static readonly TimeSpan FilterDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>Entry animation length for charts; zero when the user turned animations off.</summary>
    [ObservableProperty] private TimeSpan _chartAnimationSpeed = ChartAnimationDuration;

    // Heights that drive the scrollable horizontal bar charts (one per ~bar).
    [ObservableProperty] private double _tracksChartHeight = 480;
    [ObservableProperty] private double _artistsChartHeight = 480;
    [ObservableProperty] private double _albumsChartHeight = 480;
    [ObservableProperty] private double _showsChartHeight = 220;
    [ObservableProperty] private double _episodesChartHeight = 220;

    // Infinite-scroll paging for the horizontal bar charts: start with one page and
    // append more bars as the user scrolls toward the bottom of a chart.
    private const int BarPageSize = 60;

    // Scrollable bar chart sizing, in device-independent pixels.
    private const double BarRowHeight = 34;
    private const double BarAxisPadding = 70;
    private const double MinBarChartHeight = 220;
    private int _tracksShown;
    private int _artistsShown;
    private int _albumsShown;

    private int MaxTracks => Math.Min(ChartBuilder.MaxBars, _result.Tracks.Count);
    private int MaxArtists => Math.Min(ChartBuilder.MaxBars, _result.Artists.Count);
    private int MaxAlbums => Math.Min(ChartBuilder.MaxBars, _result.Albums.Count);

    public MainViewModel() : this(null) { }

    public MainViewModel(AppSettings? settings)
    {
        _settings = settings ?? AppSettings.Load();
        _settings.PruneMissingFolders();
        History = new HistoryViewModel(() => HasData
            ? new HistoryViewModel.Source(Records, _resultOptions ?? Filters.ToOptions())
            : null);
        Compare = new CompareViewModel(CompareSource);
        ComparePeople = new PeopleCompareViewModel(CompareSource);
        CompareArtists = new CompareArtistsViewModel(() => HasData
            ? new CompareArtistsViewModel.Source(Records, Filters.ToOptions(), _settings.FirstDayOfWeek)
            : null);
        CompareArtists.Notice += (_, message) => SetStatus(message);
        SidebarVisible = _settings.SidebarVisible;
        Metric = _settings.Metric;
        ChartAnimationSpeed = _settings.AnimateCharts ? ChartAnimationDuration : TimeSpan.Zero;
        RefreshRecentFolders();
        RefreshThemeButton();

        // Restored before anything listens for changes, so it doesn't trigger a save or a recompute.
        Filters.SetFirstDayOfWeek(_settings.FirstDayOfWeek);
        if (_settings.Filters is { } remembered)
            Filters.Apply(remembered);
        Filters.LoadPresets(_settings.FilterPresets);

        _debounce = new DispatcherTimer { Interval = FilterDebounce };
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await RecomputeAsync();
        };
        Filters.FiltersChanged += (_, _) =>
        {
            RefreshActiveFilters();
            RememberFilters();
            if (!HasData) return;
            _debounce.Stop();
            _debounce.Start();
        };
        Filters.PresetsChanged += (_, _) =>
        {
            _settings.FilterPresets = Filters.Presets.ToList();
            _settings.Save();
        };
        Filters.Notice += (_, message) => SetStatus(message);
        Filters.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FilterViewModel.CanUndo) or nameof(FilterViewModel.CanRedo))
                OnPropertyChanged(nameof(ShowFilterBar));
        };

        // Charts bake their colours into Skia paints, so they have to be rebuilt rather
        // than repainted when the palette changes.
        ThemeService.Changed += (_, _) =>
        {
            RefreshThemeButton();
            if (HasData) UpdateCharts();
        };

        RefreshActiveFilters();
        if (_settings.WasUnreadable)
        {
            SetStatus("Sortify couldn't read its settings and started with the defaults. " +
                      "The old file was kept as settings.unreadable.json in Sortify's data folder.", isError: true);
        }
    }

    /// <summary>Your plays and the sidebar's filters, for the two Compare tabs; null until a history is open.</summary>
    private CompareViewModel.Source? CompareSource() => HasData
        ? new CompareViewModel.Source(Records, Filters.ToOptions(), _settings.SessionGap, _loadedFiles,
            PlayTimeZones.From(_settings), Filters.CombineReleases)
        : null;

    // ---- Loading ---------------------------------------------------------------------------

    private const string NothingInZip =
        "No Spotify history was found in that ZIP. If it is the file Spotify sent, " +
        "it may be damaged; try downloading it again.";

    [RelayCommand]
    private async Task RunAnalysisAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select your Spotify history: the export ZIP or its JSON files",
            Filter = "Spotify history (*.json, *.zip)|*.json;*.zip|JSON files (*.json)|*.json|" +
                     "ZIP files (*.zip)|*.zip|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true)
            return;

        // A ZIP is opened up into the history files inside it; anything else is taken as
        // picked, so a renamed export still loads.
        var picked = dialog.FileNames;
        IReadOnlyList<string> FindFiles() => picked
            .SelectMany(f => ArchivePath.IsArchive(f) ? HistoryParser.FindHistoryFiles(f) : new[] { f })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // A lone ZIP goes into Recent the way a folder does.
        string? archive = picked is [var only] && ArchivePath.IsArchive(only) ? only : null;
        await LoadAsync(FindFiles, archive, NothingInZip);
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

        await OpenFolderPathAsync(dialog.FolderName);
    }

    /// <summary>
    /// Loads an export folder, or an export ZIP, by path. Used by Open Folder and the recent list.
    /// </summary>
    [RelayCommand]
    public async Task OpenFolderPathAsync(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return;

        bool isArchive = ArchivePath.IsArchive(folder);
        if (isArchive ? !File.Exists(folder) : !Directory.Exists(folder))
        {
            SetStatus(isArchive
                ? $"That file no longer exists: {folder}"
                : $"That folder no longer exists: {folder}", isError: true);
            _settings.PruneMissingFolders();
            _settings.Save();
            RefreshRecentFolders();
            return;
        }

        await LoadAsync(() => HistoryParser.FindHistoryFiles(folder), folder,
            isArchive ? NothingInZip : "No Spotify history JSON files were found in that folder.");
    }

    /// <summary>
    /// Reopens the folder used last session. Called once at startup; silently does nothing
    /// when there is no remembered folder, it has gone away, or the user turned this off.
    /// </summary>
    public async Task RestoreLastFolderAsync()
    {
        if (!_settings.ReopenLastFolder || _settings.LastFolder is not { } folder || string.IsNullOrWhiteSpace(folder))
            return;

        await LoadAsync(() => HistoryParser.FindHistoryFiles(folder), folder, nothingFound: null);
    }

    /// <summary>
    /// Loads what was dropped on the window. Folders and ZIPs are searched for history files,
    /// JSON files are read as they are, and anything else is ignored.
    /// </summary>
    public Task LoadDroppedAsync(IReadOnlyList<string> items)
    {
        IReadOnlyList<string> FindFiles() => items
            .SelectMany(item => IsExportContainer(item) ? HistoryParser.FindHistoryFiles(item)
                : IsJson(item) ? new[] { item }
                : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Only a single dropped folder or ZIP is worth remembering in Recent.
        var containers = items.Where(IsExportContainer).ToList();
        return LoadAsync(FindFiles, containers.Count == 1 ? containers[0] : null,
            "Nothing that was dropped holds Spotify listening history. Drop the ZIP " +
            "Spotify sent, the folder it unpacks to, or its JSON files.");
    }

    internal static bool IsJson(string path) =>
        string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);

    private static bool IsExportContainer(string path) =>
        Directory.Exists(path) || (ArchivePath.IsArchive(path) && File.Exists(path));

    /// <summary>
    /// Opens a made-up history for someone still waiting on their export. It isn't remembered
    /// in Recent or cached, and anything opened afterwards replaces it.
    /// </summary>
    [RelayCommand]
    private async Task OpenSampleAsync()
    {
        if (_isLoading)
            return;

        _isLoading = true;
        RefreshBusy();
        try
        {
            SetStatus("Making up a sample history...");
            var records = await Task.Run(() => SampleHistory.Generate());
            AdoptHistory(records, Array.Empty<string>(), folder: null, isSample: true);
            DescribeLoad(Array.Empty<LoadedFile>(), 0, source: null, fromCache: false);
            SetStatus($"Opened a sample history of {records.Count:N0} made-up plays. Crunching numbers...");
            await RecomputeAsync();
        }
        finally
        {
            _isLoading = false;
            RefreshBusy();
        }
    }

    private bool CanReload() => HasData && !IsSample;

    /// <summary>Re-reads the files behind the current results, bypassing the cache.</summary>
    [RelayCommand(CanExecute = nameof(CanReload))]
    private async Task ReloadAsync()
    {
        if (_loadedFiles.Count == 0)
            return;

        RecordCache.Clear();
        await LoadFilesAsync(_loadedFiles, _loadedFolder);
    }

    /// <summary>
    /// Stops whatever the status bar is currently reporting: a parse, an analysis or both.
    /// The status is set here rather than where the cancellation lands, because a recompute
    /// is also cancelled every time a newer one supersedes it, and that is not worth
    /// announcing.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void CancelLoad()
    {
        _loadCts?.Cancel();
        _analysisCts?.Cancel();
        SetStatus("Cancelled. Change a filter or press F5 to run again.");
    }

    /// <summary>
    /// Parses the given history files and runs analysis. <paramref name="folder"/> is the
    /// folder or ZIP they came from, remembered in Recent.
    /// </summary>
    public Task LoadFilesAsync(IReadOnlyList<string> filePaths, string? folder = null) =>
        filePaths.Count == 0 ? Task.CompletedTask : LoadAsync(() => filePaths, folder, nothingFound: null);

    /// <summary>
    /// Runs <paramref name="findFiles"/>, then parses what it found and runs analysis. The
    /// search happens off the UI thread and under the same progress bar and Cancel button as
    /// the parse, since walking a big folder can take a while. <paramref name="nothingFound"/>
    /// is the error shown when it finds no files; null says nothing.
    /// </summary>
    internal async Task LoadAsync(Func<IReadOnlyList<string>> findFiles, string? folder, string? nothingFound)
    {
        if (_isLoading)
            return;

        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        var (statusBefore, errorBefore) = (StatusText, StatusIsError);
        _isLoading = true;
        RefreshBusy();
        IsProgressIndeterminate = true;
        ProgressValue = 0;

        try
        {
            SetStatus("Looking for your Spotify history...");
            var filePaths = await Task.Run(findFiles, token);
            token.ThrowIfCancellationRequested();
            if (filePaths.Count == 0)
            {
                if (nothingFound is null)
                    SetStatus(statusBefore, errorBefore);
                else
                    SetStatus(nothingFound, isError: true);
                return;
            }

            // Re-reading an unchanged export is the common case (relaunching the app, or
            // reopening the same folder), and parsing it again costs seconds for nothing.
            var timeZones = PlayTimeZones.From(_settings);
            SetStatus("Checking for cached results...");
            var cached = await Task.Run(() => RecordCache.TryLoadHistory(filePaths, timeZones), token);
            token.ThrowIfCancellationRequested();
            if (cached is { Records.Count: > 0 })
            {
                AdoptHistory(cached.Records.ToList(), filePaths, folder);
                _loadedTimeZones = timeZones.CacheKey;
                DescribeLoad(cached.Files, cached.DuplicatesRemoved, folder, fromCache: true);
                SetStatus($"Loaded {Plural.Of(_rawRecords.Count, "play")} from cache. Crunching numbers...");
                await RecomputeAsync();
                return;
            }

            IsProgressIndeterminate = false;
            var progress = new Progress<ParseProgress>(p =>
            {
                ProgressValue = p.Percent;
                StatusText = p.Message;
                StatusIsError = false;
            });

            var parsed = await new HistoryParser(timeZones).ParseAsync(filePaths, progress, token);
            if (parsed.Records.Count == 0)
            {
                // A stray file dropped on the window shouldn't take away the history already open.
                string problem = parsed.Warnings.Count > 0
                    ? $"No valid listening data found. {parsed.Warnings[0]}"
                    : "No valid listening data found in the selected files.";
                SetStatus(HasData ? $"{problem} Your current history is still open." : problem, isError: true);
                return;
            }

            AdoptHistory(parsed.Records, filePaths, folder);
            _loadedTimeZones = timeZones.CacheKey;
            DescribeLoad(parsed.Files, parsed.DuplicatesRemoved, folder, fromCache: false);
            int problems = parsed.Warnings.Count + parsed.SkippedFiles.Count;
            string warn = problems > 0 ? $" ({Plural.Of(problems, "file")} skipped)" : string.Empty;
            IsProgressIndeterminate = true;
            SetStatus($"Loaded {Plural.Of(_rawRecords.Count, "play")} from {Plural.Of(filePaths.Count, "file")}{warn}. Crunching numbers...");
            await RecomputeAsync();

            // Said after the analysis rather than before it, which would overwrite it at once.
            if (parsed.DuplicatesRemoved > 0 && !StatusIsError)
            {
                SetStatus($"{StatusText} Left out {Plural.Of(parsed.DuplicatesRemoved, "play")} " +
                          "that more than one of the loaded files held.");
            }

            // Save after analysis so the user isn't waiting on disk I/O to see results.
            var toCache = _rawRecords;
            _ = Task.Run(() => RecordCache.TrySave(filePaths, toCache, timeZones, parsed.Files, parsed.DuplicatesRemoved),
                CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Loading cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Each file's own problems are caught as it is read; this is the search for them
            // failing, such as a folder deleted or a network drive dropping out partway through.
            string problem = $"Couldn't read your Spotify history: {ex.Message}";
            SetStatus(HasData ? $"{problem} Your current history is still open." : problem, isError: true);
        }
        finally
        {
            _isLoading = false;
            RefreshBusy();
            IsProgressIndeterminate = true;
            ProgressValue = 0;
        }
    }

    /// <summary>
    /// Makes freshly read plays the history on screen. Only a load that found plays gets here,
    /// so a folder or ZIP is remembered in Recent (and reopened at the next launch) once it has
    /// actually loaded.
    /// </summary>
    private void AdoptHistory(List<PlayRecord> records, IReadOnlyList<string> filePaths, string? folder, bool isSample = false)
    {
        // Reloading the same export keeps the artists and periods being compared; a different
        // one starts again from its own.
        bool newHistory = isSample != IsSample || !filePaths.SequenceEqual(_loadedFiles, StringComparer.OrdinalIgnoreCase);
        if (newHistory)
            CompareArtists.StartOver();

        _rawRecords = records;
        _combinedRecords = null;
        _loadedFiles = filePaths;
        _loadedFolder = folder;
        IsSample = isSample;
        _cohorts = CohortBreakdown.Empty;
        if (folder is not null)
        {
            _settings.RememberFolder(folder);
            _settings.Save();
            RefreshRecentFolders();
        }

        OfferChoicesForHistory(newHistory);
        HasData = true;
    }

    private void RefreshBusy() => IsBusy = _isLoading || _pendingAnalyses > 0;

    /// <summary>Records what the history just adopted was read from, and words the status bar's link to it.</summary>
    private void DescribeLoad(IReadOnlyList<LoadedFile> files, int duplicatesRemoved, string? source, bool fromCache)
    {
        var (first, last) = PlayRecord.DateSpan(_rawRecords);
        LoadedHistory = new LoadedHistory(files, duplicatesRemoved, source, fromCache, IsSample, _rawRecords.Count, first, last);
        int read = files.Count(f => f.Format != LoadedFileFormat.Skipped);
        int skipped = files.Count - read;
        LoadedFilesText = IsSample
            ? "About this sample"
            : $"{Plural.Of(read, "file")} loaded{(skipped > 0 ? $", {skipped:N0} skipped" : "")}";
    }

    /// <summary>
    /// Offers the quick date ranges, devices and countries that fit the history just loaded.
    /// Read off the raw records rather than the filtered results, so filtering to one year
    /// or one device doesn't take the other choices away. A different export from the one
    /// before also starts the Compare Periods tab over on its own periods.
    /// </summary>
    private void OfferChoicesForHistory(bool newHistory)
    {
        DateTime? last = null;
        var years = new SortedSet<int>();
        var platformMs = new Dictionary<string, long>(StringComparer.Ordinal);
        var countryMs = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var artistMs = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var r in _rawRecords)
        {
            CollectionsMarshal.GetValueRefOrAddDefault(artistMs, r.DisplayArtist, out _) += r.MsPlayed;
            if (r.Platform.Length > 0)
                CollectionsMarshal.GetValueRefOrAddDefault(platformMs, r.Platform, out _) += r.MsPlayed;
            if (r.Country.Length > 0)
                CollectionsMarshal.GetValueRefOrAddDefault(countryMs, r.Country, out _) += r.MsPlayed;

            if (r.Timestamp == DateTime.MinValue)
                continue;
            years.Add(r.Timestamp.Year);
            if (last is null || r.Timestamp > last)
                last = r.Timestamp;
        }

        Filters.SetAvailableDates(last, years);
        Compare.SetHistory(last, years.ToList(), resetPeriods: newHistory);

        var devices = platformMs
            .GroupBy(kv => Devices.FamilyOf(kv.Key))
            .OrderByDescending(g => g.Sum(kv => kv.Value))
            .Select(g => (g.Key, g.Key, g.Key));
        var countries = countryMs
            .OrderByDescending(kv => kv.Value)
            .Select(kv => (kv.Key, kv.Key.ToUpperInvariant(), CountryName(kv.Key)));
        Filters.SetAvailablePlayback(devices, countries);
        Filters.SetKnownArtists(artistMs.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList());
    }

    /// <summary>"Canada (CA)" for "CA", or the code alone when .NET doesn't know it.</summary>
    internal static string CountryName(string code)
    {
        try
        {
            return $"{new RegionInfo(code).EnglishName} ({code.ToUpperInvariant()})";
        }
        catch (ArgumentException)
        {
            return code.ToUpperInvariant();
        }
    }

    // ---- Chart options ---------------------------------------------------------------------

    /// <summary>Changes the bucket size of the listening-over-time chart.</summary>
    public void SetOverTimeGranularity(ChartBuilder.TimeGranularity granularity)
    {
        if (_overTimeGranularity == granularity)
            return;
        _overTimeGranularity = granularity;
        if (HasData)
            OverTime = ChartBuilder.OverTime(_result, granularity, Metric, _settings.FirstDayOfWeek);
    }

    partial void OnMetricChanged(Metric value)
    {
        if (_settings.Metric != value)
        {
            _settings.Metric = value;
            _settings.Save();
        }

        Compare.Metric = value;
        ComparePeople.Metric = value;
        CompareArtists.Metric = value;
        if (HasData)
        {
            UpdateCollections();
            // The years keep their order but swap which leader columns they show; handing the
            // grid a fresh list clears a sort on a column that just hid, which would otherwise
            // go on ordering the rows with no arrow left to say so.
            Years = _result.Years.ToList();
            UpdateCharts();
        }
    }

    // ---- Appearance and preferences ----------------------------------------------------------

    [RelayCommand]
    private void ToggleTheme()
    {
        var next = ThemeService.Next();
        ThemeService.Apply(next);
        _settings.Theme = next;
        _settings.Save();
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        SidebarVisible = !SidebarVisible;
        _settings.SidebarVisible = SidebarVisible;
        _settings.Save();
    }

    /// <summary>Re-reads preferences after the settings dialog closes and applies them.</summary>
    public async Task ApplySettingsChangesAsync()
    {
        ChartAnimationSpeed = _settings.AnimateCharts ? ChartAnimationDuration : TimeSpan.Zero;
        Filters.SetFirstDayOfWeek(_settings.FirstDayOfWeek);
        RefreshActiveFilters();
        RefreshRecentFolders();
        _ = ComparePeople.ReadTheirsAgainIfTimesChangedAsync();

        // Play times are converted as an export is read, so a new time zone setting means
        // reading it again. The sample's times are made up in local time to begin with.
        if (HasData && !IsSample && _loadedFiles.Count > 0 &&
            PlayTimeZones.From(_settings).CacheKey != _loadedTimeZones)
        {
            await LoadFilesAsync(_loadedFiles, _loadedFolder);
            return;
        }

        if (HasData)
            await RecomputeAsync();
    }

    private void RefreshRecentFolders()
    {
        RecentFolders.Clear();
        foreach (var folder in _settings.RecentFolders)
            RecentFolders.Add(folder);
    }

    private void RefreshThemeButton()
    {
        // Segoe MDL2 Assets: E706 is a sun, E708 a moon.
        ThemeGlyph = ThemeService.IsDark ? "" : "";
        ThemeTooltip = ThemeService.IsDark ? "Switch to the light theme" : "Switch to the dark theme";
    }

    /// <summary>
    /// Writes the filters that carry over between launches, when they changed. Most filter
    /// edits touch none of them, so most calls write nothing.
    /// </summary>
    private void RememberFilters()
    {
        var remembered = Filters.RememberedSnapshot();
        if (_settings.Filters is { } saved && saved.SameFiltersAs(remembered))
            return;

        _settings.Filters = remembered;
        _settings.Save();
    }

    private void RefreshActiveFilters()
    {
        ActiveFilters.Clear();
        foreach (var chip in Filters.Chips())
            ActiveFilters.Add(chip);
        HasActiveFilters = ActiveFilters.Count > 0;
    }

    // ---- Grid-driven exclusions ----------------------------------------------------------

    public void ExcludeArtistsFromGrid(IEnumerable<string> artists) => Filters.ExcludeArtists(artists);
    public void FocusOnArtistsFromGrid(IEnumerable<string> artists) => Filters.FocusOnArtists(artists);
    public void ExcludeTracksFromGrid(IEnumerable<string> tracks) => Filters.ExcludeTracks(tracks);

    /// <summary>
    /// Puts Spotify links for <paramref name="tracks"/> on the clipboard via
    /// <paramref name="copy"/>, and says in the status bar what was copied and what wasn't.
    /// </summary>
    public void CopySpotifyLinks(IEnumerable<TrackStat> tracks, Func<string, bool> copy)
    {
        var links = SpotifyLink.ForTracks(tracks);
        if (links.Copied == 0)
        {
            SetStatus(links.Missing == 0
                ? "There were no tracks to copy."
                : "None of those tracks has a Spotify link. Only the extended streaming history records them.",
                isError: true);
            return;
        }

        if (!copy(links.Text))
        {
            SetStatus("Could not copy the links: another program is holding the clipboard. Try again.", isError: true);
            return;
        }

        string missing = links.Missing == 0
            ? string.Empty
            : $" {Plural.Of(links.Missing, "track")} {(links.Missing == 1 ? "has" : "have")} no link in the export and {(links.Missing == 1 ? "was" : "were")} left out.";
        SetStatus($"Copied {Plural.Of(links.Copied, "Spotify link")}. " +
                  $"Paste {(links.Copied == 1 ? "it" : "them")} into a playlist in the Spotify desktop app.{missing}");
    }

    // ---- Drill-down ------------------------------------------------------------------------

    /// <summary>
    /// Builds the focused breakdown for one grid row, under the filters currently in effect.
    /// Returns null when there is nothing loaded to drill into.
    /// </summary>
    public async Task<DetailResult?> BuildDetailAsync(DetailScope scope, string title, string subtitle)
    {
        if (!HasData || _rawRecords.Count == 0)
            return null;

        return await DetailEngine.BuildAsync(Records, Filters.ToOptions(), scope, title, subtitle, _result, Metric);
    }

    /// <summary>
    /// Works out a year's summary card under the sidebar filters, whatever dates the sidebar
    /// has. Null when there is nothing loaded.
    /// </summary>
    public async Task<YearReview?> BuildYearReviewAsync(int year)
    {
        if (!HasData || _rawRecords.Count == 0)
            return null;

        return await YearReviewBuilder.BuildAsync(Records, Filters.ToOptions(), year, CardFilterNote(), _settings.SessionGap);
    }

    /// <summary>
    /// Works out the summary card for the dates set in the sidebar, or for the whole history
    /// when none are, under the other filters. Null when there is nothing loaded.
    /// </summary>
    public async Task<YearReview?> BuildSummaryCardAsync()
    {
        if (!HasData || _rawRecords.Count == 0)
            return null;

        var options = Filters.ToOptions();
        return await YearReviewBuilder.BuildForDatesAsync(Records, options, options.StartDate, options.EndDate,
            CardFilterNote(), _settings.SessionGap, _settings.FirstDayOfWeek);
    }

    /// <summary>Whether there is anything to put on a summary card.</summary>
    public bool CanMakeSummaryCard => CanExport();

    // Dates are left out: the card's heading already says which days it covers.
    private string CardFilterNote()
    {
        var others = Filters.Describe(includeDates: false).ToList();
        return others.Count == 0 ? string.Empty : "Filtered: " + string.Join("; ", others);
    }

    // ---- Analysis --------------------------------------------------------------------------

    private async Task RecomputeAsync()
    {
        if (!HasData) return;

        Filters.CommitHistory();

        // Cancel the previous pass but keep its source alive: the running task may still be
        // reading the token, and disposing it out from under that is a race.
        var previous = _analysisCts;
        _analysisCts = new CancellationTokenSource();
        previous?.Cancel();
        var token = _analysisCts.Token;

        var options = Filters.ToOptions();
        var described = Filters.Describe().ToList();
        _pendingAnalyses++;
        RefreshBusy();
        try
        {
            // Folding re-releases goes through every play, so it is done off the UI thread the
            // first time; a history adopted in the meantime makes the result moot.
            if (Filters.CombineReleases && _combinedRecords is null)
            {
                var raw = _rawRecords;
                var combined = await Task.Run(() => ReleaseNames.Combine(raw));
                if (ReferenceEquals(raw, _rawRecords))
                    _combinedRecords ??= combined;
            }

            var suggesting = RefreshSuggestionsAsync();
            AnalysisResult result;
            try
            {
                result = await AnalysisEngine.AnalyzeAsync(Records, options, _settings.SessionGap, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                previous?.Dispose();
            }

            // A newer recompute may have started while this one ran; never let a stale
            // result overwrite the current one.
            if (token.IsCancellationRequested)
                return;
            _result = result;
            _resultFilters = described;
            _resultOptions = options;
            if (!ShowCohorts)
                _cohorts = CohortBreakdown.Empty;

            TableSuggestions = SearchSuggestions.FromResult(_result);
            UpdateCollections();
            UpdateSummary();
            Insights = InsightText.For(_result);
            OnPropertyChanged(nameof(BiggestDay));
            UpdateCharts();
            Calendar.Update(_result.PlaytimeByDay, _settings.FirstDayOfWeek);
            NotifyExportsChanged();
            if (IsJumpOpen)
                RefreshJump();

            SetStatus(_result.TotalPlays == 0
                ? Filters.HasInvalidDateRange
                    ? "No plays match: the From date is after the To date."
                    : "No plays match the current filters."
                : $"Showing {Plural.Of(_result.TotalPlays, "play")} across {Plural.Of(_result.UniqueTracks, "track")}, " +
                  $"{Plural.Of(_result.UniqueArtists, "artist")} and {Plural.Of(_result.UniqueAlbums, "album")}.");

            Compare.Invalidate();
            ComparePeople.Invalidate();
            History.Invalidate();
            if (ShowCohorts)
                _ = RefreshCohortsAsync();
            await CompareArtists.UpdateAsync(RankedArtists);
            await suggesting;
        }
        finally
        {
            _pendingAnalyses--;
            RefreshBusy();
        }
    }

    /// <summary>The plays <see cref="Suggestions"/> were last built from.</summary>
    private IReadOnlyList<PlayRecord>? _suggestedFrom;

    /// <summary>
    /// Builds the sidebar's name suggestions off the UI thread from the plays the views read, so
    /// with re-releases combined a remaster isn't offered under a name no play has any more.
    /// Only a new history or a change to that option builds them again.
    /// </summary>
    private async Task RefreshSuggestionsAsync()
    {
        var records = Records;
        if (ReferenceEquals(records, _suggestedFrom))
            return;

        _suggestedFrom = records;
        var built = await Task.Run(() => SearchSuggestions.Build(records));
        if (ReferenceEquals(records, _suggestedFrom))
            Suggestions = built;
    }

    private void UpdateCollections()
    {
        ApplyTrackQuickFilter();
        ApplyArtistQuickFilter();
        ApplyAlbumQuickFilter();
        ApplyShowQuickFilter();

        Years = _result.Years;
        SkippedTracks = _result.SkippedTracks;
        ForgottenTracks = _result.ForgottenTracks;
        ForgottenArtists = _result.ForgottenArtists;
        Months = _result.Months.Reverse().Select(m => MonthRow.For(m, Metric)).ToList();
        MonthlyLeaderText = InsightText.MonthlyLeader(_result.Months, Metric);
        Milestones = _result.Milestones;
        OnRepeat = _result.OnRepeat;
        HasPodcastData = _result.Shows.Count > 0;

        TopTracks = RankedTracks.Take(5)
            .Select((t, i) => RankedItem.For(i + 1, t.Track, t.Artist, t.TotalTime, t.PlayCount, Metric))
            .ToList();
        TopArtists = RankedArtists.Take(5)
            .Select((a, i) => RankedItem.For(i + 1, a.Artist, string.Empty, a.TotalTime, a.PlayCount, Metric))
            .ToList();
        TopAlbums = RankedAlbums.Take(5)
            .Select((a, i) => RankedItem.For(i + 1, a.Album, a.Artist, a.TotalTime, a.PlayCount, Metric))
            .ToList();
    }

    // The rows in the order the metric ranks them. Both orders come pre-sorted from the
    // analysis, so switching never sorts tens of thousands of rows on the UI thread.
    private IReadOnlyList<TrackStat> RankedTracks => Metric == Metric.Plays ? _result.TracksByPlayCount : _result.Tracks;
    private IReadOnlyList<ArtistStat> RankedArtists => Metric == Metric.Plays ? _result.ArtistsByPlayCount : _result.Artists;
    private IReadOnlyList<AlbumStat> RankedAlbums => Metric == Metric.Plays ? _result.AlbumsByPlayCount : _result.Albums;
    private IReadOnlyList<ShowStat> RankedShows => Metric == Metric.Plays ? _result.ShowsByPlayCount : _result.Shows;
    private IReadOnlyList<EpisodeStat> RankedEpisodes => Metric == Metric.Plays ? _result.EpisodesByPlayCount : _result.Episodes;

    // ---- Quick filters ------------------------------------------------------------------------

    partial void OnTrackQuickFilterChanged(string value) => ApplyTrackQuickFilter();
    partial void OnArtistQuickFilterChanged(string value) => ApplyArtistQuickFilter();
    partial void OnAlbumQuickFilterChanged(string value) => ApplyAlbumQuickFilter();
    partial void OnShowQuickFilterChanged(string value) => ApplyShowQuickFilter();

    private void ApplyTrackQuickFilter() =>
        Tracks = Narrow(RankedTracks, TrackQuickFilter, t => t.Track, t => t.Artist);

    private void ApplyArtistQuickFilter() =>
        Artists = Narrow(RankedArtists, ArtistQuickFilter, a => a.Artist);

    private void ApplyAlbumQuickFilter() =>
        Albums = Narrow(RankedAlbums, AlbumQuickFilter, a => a.Album, a => a.Artist);

    private void ApplyShowQuickFilter()
    {
        Shows = Narrow(RankedShows, ShowQuickFilter, s => s.Show);
        Episodes = Narrow(RankedEpisodes, ShowQuickFilter, e => e.Episode, e => e.Show);
    }

    /// <summary>
    /// Case-insensitive substring match over the given fields. Returns the original list
    /// untouched when nothing is typed, so the common case allocates nothing.
    /// </summary>
    private static IReadOnlyList<T> Narrow<T>(IReadOnlyList<T> source, string term, params Func<T, string>[] fields)
    {
        if (string.IsNullOrWhiteSpace(term))
            return source;

        string needle = term.Trim();
        var matches = new List<T>();
        foreach (var item in source)
        {
            foreach (var field in fields)
            {
                if (field(item).Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(item);
                    break;
                }
            }
        }
        return matches;
    }

    // ---- New or familiar ---------------------------------------------------------------------------

    /// <summary>
    /// True while the Years chart splits each year by when its music was first heard. That
    /// takes two more passes over the plays, so it is only worked out while shown.
    /// </summary>
    [ObservableProperty] private bool _showCohorts;

    [ObservableProperty] private ChartData _cohortChart = ChartData.Empty;
    [ObservableProperty] private string _cohortText = string.Empty;

    private CohortBreakdown _cohorts = CohortBreakdown.Empty;
    private CancellationTokenSource? _cohortCts;

    partial void OnShowCohortsChanged(bool value)
    {
        if (value)
            _ = RefreshCohortsAsync();
    }

    private async Task RefreshCohortsAsync()
    {
        if (!HasData)
            return;

        var previous = _cohortCts;
        _cohortCts = new CancellationTokenSource();
        previous?.Cancel();
        var token = _cohortCts.Token;

        try
        {
            var cohorts = await DiscoveryCohorts.BuildAsync(Records, _resultOptions ?? Filters.ToOptions(), token);
            if (token.IsCancellationRequested)
                return;

            _cohorts = cohorts;
            CohortChart = ChartBuilder.Cohorts(_cohorts, _result.Years, Metric);
            CohortText = InsightText.Cohorts(_cohorts, Metric);
        }
        catch (OperationCanceledException)
        {
            // A newer refresh took over.
        }
        finally
        {
            previous?.Dispose();
        }
    }

    // ---- Jump to (Ctrl+K) ---------------------------------------------------------------------

    /// <summary>How many matches of each kind (artists, tracks...) the jump box lists.</summary>
    public const int JumpResultsPerKind = 6;

    [ObservableProperty] private bool _isJumpOpen;
    [ObservableProperty] private string _jumpQuery = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJumpResults))]
    private IReadOnlyList<JumpResult> _jumpResults = Array.Empty<JumpResult>();

    public bool HasJumpResults => JumpResults.Count > 0;
    [ObservableProperty] private JumpResult? _selectedJump;
    [ObservableProperty] private string _jumpHint = string.Empty;

    partial void OnJumpQueryChanged(string value) => RefreshJump();

    /// <summary>Opens the jump box with an empty query. Returns false when there is nothing to search yet.</summary>
    public bool OpenJump()
    {
        if (!HasData)
        {
            SetStatus("Load your history first, then press Ctrl+K to jump to anything in it.");
            return false;
        }

        JumpQuery = string.Empty;
        RefreshJump();
        IsJumpOpen = true;
        return true;
    }

    public void CloseJump() => IsJumpOpen = false;

    /// <summary>Moves the highlighted match up or down, stopping at either end.</summary>
    public void MoveJumpSelection(int delta)
    {
        if (JumpResults.Count == 0)
            return;

        int at = SelectedJump is null ? -1 : JumpResults.ToList().IndexOf(SelectedJump);
        SelectedJump = JumpResults[Math.Clamp(at + delta, 0, JumpResults.Count - 1)];
    }

    private void RefreshJump()
    {
        string query = JumpQuery.Trim();
        JumpResults = SearchForJump(_result, query);
        SelectedJump = JumpResults.FirstOrDefault();
        JumpHint = query.Length == 0
            ? "Type part of a track, artist, album or podcast name. Up and Down pick a match, Enter opens it, Esc closes."
            : JumpResults.Count == 0
                ? $"Nothing called “{query}” in the current results. The filters in the sidebar still apply."
                : string.Empty;
    }

    /// <summary>
    /// The best matches of each kind in the current results. A name that is the query, then
    /// one that starts with it, then one with a word starting with it, beat one that merely
    /// contains it; ties go to whatever was listened to longest.
    /// </summary>
    internal static IReadOnlyList<JumpResult> SearchForJump(AnalysisResult r, string query, int perKind = JumpResultsPerKind)
    {
        query = query.Trim();
        if (query.Length == 0)
            return Array.Empty<JumpResult>();

        var results = new List<JumpResult>();
        results.AddRange(Best(r.Artists, a => a.Artist, a => a.TotalMsPlayed)
            .Select(a => new JumpResult("Artist", a.Artist, Plural.Of(a.PlayCount, "play"), TimeFormat.Friendly(a.TotalTime), a)));
        results.AddRange(Best(r.Tracks, t => t.Track, t => t.TotalMsPlayed)
            .Select(t => new JumpResult("Track", t.Track, t.Artist, TimeFormat.Friendly(t.TotalTime), t)));
        results.AddRange(Best(r.Albums, a => a.Album, a => a.TotalMsPlayed)
            .Select(a => new JumpResult("Album", a.Album, a.Artist, TimeFormat.Friendly(a.TotalTime), a)));
        results.AddRange(Best(r.Shows, s => s.Show, s => s.TotalMsPlayed)
            .Select(s => new JumpResult(s.Kind == ContentKind.Audiobook ? "Audiobook" : "Podcast", s.Show,
                Plural.Of(s.EpisodeCount, "episode"), TimeFormat.Friendly(s.TotalTime), s)));
        results.AddRange(Best(r.Episodes, e => e.Episode, e => e.TotalMsPlayed)
            .Select(e => new JumpResult("Episode", e.Episode, e.Show, TimeFormat.Friendly(e.TotalTime), e)));
        return results;

        List<T> Best<T>(IReadOnlyList<T> items, Func<T, string> name, Func<T, long> ms) =>
            NameMatch.Best(items, item => NameMatch.Rank(name(item), query), perKind, (a, b) => ms(b).CompareTo(ms(a)));
    }

    // ---- Summary text -------------------------------------------------------------------------

    private void UpdateSummary()
    {
        TotalTimeText = TimeFormat.Friendly(_result.TotalTime);
        TotalPlaysText = _result.TotalPlays.ToString("N0");
        UniqueArtistsText = _result.UniqueArtists.ToString("N0");
        UniqueTracksText = _result.UniqueTracks.ToString("N0");
        DateRangeText = _result.FirstListen is { } f && _result.LastListen is { } l
            ? $"{TimeFormat.Timestamp(f)}  to  {TimeFormat.Timestamp(l)}"
            : "-";

        var options = _resultOptions ?? Filters.ToOptions();
        var range = _result.TotalPlays == 0
            ? null
            : CardRange.For(options.StartDate, options.EndDate, _result.FirstListen, _result.LastListen);
        SummaryCardText = range switch
        {
            null => "Summary card",
            { AllTime: true } => "Summary card for all time",
            _ => $"Summary card for {range.Title}",
        };
    }

    // ---- Charts ------------------------------------------------------------------------------

    private void UpdateCharts()
    {
        ByHour = ChartBuilder.ByHour(_result, Metric);
        ByDayOfWeek = ChartBuilder.ByDayOfWeek(_result, Metric, _settings.FirstDayOfWeek);
        Heat = ChartBuilder.DowHourHeat(_result, Metric, _settings.FirstDayOfWeek);
        OverTime = ChartBuilder.OverTime(_result, _overTimeGranularity, Metric, _settings.FirstDayOfWeek);
        ByYear = ChartBuilder.PerYear(_result, Metric);
        RankChart = ChartBuilder.RankHistory(_result, Metric);
        HasRankHistory = _result.RankHistory.Count > 0;
        Discovery = ChartBuilder.NewArtistsByMonth(_result);
        Skipped = ChartBuilder.TopSkippedTracks(_result);
        ArtistShareSeries = ChartBuilder.ArtistShare(_result, Metric);
        ArtistShareLegend = LegendEntry.For(ChartBuilder.ArtistShareSlicesOf(_result, Metric));
        ReasonEndSeries = ChartBuilder.ReasonEndShare(_result);
        ReasonEndLegend = LegendEntry.For(ChartBuilder.ReasonEndSlicesOf(_result));
        PlatformSeries = ChartBuilder.PlatformShare(_result, Metric);
        PlatformLegend = LegendEntry.For(ChartBuilder.PlatformSlicesOf(_result, Metric));
        CountrySeries = ChartBuilder.CountryShare(_result, Metric);
        CountryLegend = LegendEntry.For(ChartBuilder.CountrySlicesOf(_result, Metric));

        // Size these to their content: most libraries hold only a handful of shows, and a
        // fixed-height chart would space three bars across half a screen.
        ShowsChart = ChartBuilder.TopShows(_result, metric: Metric);
        EpisodesChart = ChartBuilder.TopEpisodes(_result, metric: Metric);
        ShowsChartHeight = BarHeight(Math.Min(ChartBuilder.PodcastBars, _result.Shows.Count));
        EpisodesChartHeight = BarHeight(Math.Min(ChartBuilder.PodcastBars, _result.Episodes.Count));
        CompareArtists.Redraw();
        CohortChart = ChartBuilder.Cohorts(_cohorts, _result.Years, Metric);
        CohortText = InsightText.Cohorts(_cohorts, Metric);

        // Reset the scrollable bar charts to their first page; LoadMore* append the rest.
        _tracksShown = Math.Min(BarPageSize, MaxTracks);
        _artistsShown = Math.Min(BarPageSize, MaxArtists);
        _albumsShown = Math.Min(BarPageSize, MaxAlbums);
        BuildTrackCharts();
        BuildArtistCharts();
        BuildAlbumCharts();
    }

    private static double BarHeight(int bars) => Math.Max(MinBarChartHeight, bars * BarRowHeight + BarAxisPadding);

    private void BuildTrackCharts()
    {
        TracksChart = ChartBuilder.TopTracks(_result, _tracksShown, Metric);
        TracksChartHeight = BarHeight(_tracksShown);
    }

    private void BuildArtistCharts()
    {
        ArtistsChart = ChartBuilder.TopArtists(_result, _artistsShown, Metric);
        ArtistsChartHeight = BarHeight(_artistsShown);
    }

    private void BuildAlbumCharts()
    {
        AlbumsChart = ChartBuilder.TopAlbums(_result, _albumsShown, Metric);
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

    // ---- Exports -------------------------------------------------------------------------------

    private bool CanExport() => HasData && _result.TotalPlays > 0;

    private bool CanExportPodcasts() => HasData && _result.Shows.Count > 0;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportTxtAsync() =>
        ExportAsync("Text files (*.txt)|*.txt", ".txt", "Sortify_results",
            path => ExportService.SaveTxtAsync(path, _result, _resultFilters));

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportMarkdownAsync() =>
        ExportAsync("Markdown files (*.md)|*.md", ".md", "Sortify_report",
            path => ExportService.SaveMarkdownAsync(path, _result, _resultFilters));

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportJsonAsync() =>
        ExportAsync("JSON files (*.json)|*.json", ".json", "Sortify_results",
            path => ExportService.SaveJsonAsync(path, _result, _resultFilters));

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportTracksCsvAsync() =>
        ExportAsync("CSV files (*.csv)|*.csv", ".csv", "Sortify_tracks",
            path => ExportService.SaveTracksCsvAsync(path, _result));

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportArtistsCsvAsync() =>
        ExportAsync("CSV files (*.csv)|*.csv", ".csv", "Sortify_artists",
            path => ExportService.SaveArtistsCsvAsync(path, _result));

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportAlbumsCsvAsync() =>
        ExportAsync("CSV files (*.csv)|*.csv", ".csv", "Sortify_albums",
            path => ExportService.SaveAlbumsCsvAsync(path, _result));

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportYearsCsvAsync() =>
        ExportAsync("CSV files (*.csv)|*.csv", ".csv", "Sortify_years",
            path => ExportService.SaveYearsCsvAsync(path, _result));

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportPlaysCsvAsync() =>
        ExportAsync("CSV files (*.csv)|*.csv", ".csv", "Sortify_plays",
            path => ExportService.SavePlaysCsvAsync(path, FilterEngine.Apply(Records, _resultOptions ?? Filters.ToOptions())));

    [RelayCommand(CanExecute = nameof(CanExportPodcasts))]
    private Task ExportShowsCsvAsync() =>
        ExportAsync("CSV files (*.csv)|*.csv", ".csv", "Sortify_shows",
            path => ExportService.SaveShowsCsvAsync(path, _result));

    /// <summary>
    /// Shared save-file plumbing. A failed write is reported in the status bar: the disk
    /// being full or the folder being read-only is the user's problem to fix, not a reason
    /// to tear the app down.
    /// </summary>
    private async Task ExportAsync(string filter, string extension, string defaultName, Func<string, Task> write)
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            DefaultExt = extension,
            FileName = defaultName + extension,
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await write(dialog.FileName);
            SetStatus($"Saved {Path.GetFileName(dialog.FileName)} to {Path.GetDirectoryName(dialog.FileName)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            SetStatus($"Could not save that file: {ex.Message}", isError: true);
        }
    }

    partial void OnHasDataChanged(bool value)
    {
        NotifyExportsChanged();
        ReloadCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSampleChanged(bool value) => ReloadCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value) => CancelLoadCommand.NotifyCanExecuteChanged();

    partial void OnSidebarVisibleChanged(bool value) => _settings.SidebarVisible = value;

    private void NotifyExportsChanged()
    {
        ExportTxtCommand.NotifyCanExecuteChanged();
        ExportMarkdownCommand.NotifyCanExecuteChanged();
        ExportJsonCommand.NotifyCanExecuteChanged();
        ExportTracksCsvCommand.NotifyCanExecuteChanged();
        ExportArtistsCsvCommand.NotifyCanExecuteChanged();
        ExportAlbumsCsvCommand.NotifyCanExecuteChanged();
        ExportYearsCsvCommand.NotifyCanExecuteChanged();
        ExportShowsCsvCommand.NotifyCanExecuteChanged();
        ExportPlaysCsvCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanMakeSummaryCard));
    }

    /// <summary>Sets the status line, and whether it should be shown as a problem.</summary>
    public void SetStatus(string text, bool isError = false)
    {
        StatusText = text;
        StatusIsError = isError;
    }
}

/// <summary>
/// One row of an Overview top-five list. <see cref="Value"/> is the amount it was ranked by,
/// shown first; <see cref="Detail"/> is the other one.
/// </summary>
public sealed record RankedItem(int Rank, string Name, string Secondary, string Value, string Detail)
{
    public static RankedItem For(int rank, string name, string secondary, TimeSpan time, int plays, Metric metric)
    {
        string timeText = TimeFormat.Friendly(time);
        string playsText = Plural.Of(plays, "play");
        return metric == Metric.Plays
            ? new RankedItem(rank, name, secondary, playsText, timeText)
            : new RankedItem(rank, name, secondary, timeText, playsText);
    }
}

/// <summary>
/// One month on the Trends tab's "#1 each month" list: the artist and track that led it by the
/// chosen metric. Each amount comes as text to show and a number for the grid to sort by.
/// </summary>
public sealed class MonthRow
{
    /// <summary>The first day of the month.</summary>
    public required DateTime Month { get; init; }
    public required string TopArtist { get; init; }
    public required string TopTrack { get; init; }
    public required string TopTrackArtist { get; init; }

    public double ArtistAmount { get; init; }
    public string ArtistAmountText { get; init; } = string.Empty;
    public double TrackAmount { get; init; }
    public string TrackAmountText { get; init; } = string.Empty;
    public double Total { get; init; }
    public string TotalText { get; init; } = string.Empty;

    public static MonthRow For(MonthStat m, Metric metric) => metric == Metric.Plays
        ? new MonthRow
        {
            Month = m.Month,
            TopArtist = m.MostPlayedArtist,
            TopTrack = m.MostPlayedTrack,
            TopTrackArtist = m.MostPlayedTrackArtist,
            ArtistAmount = m.MostPlayedArtistPlays,
            ArtistAmountText = m.MostPlayedArtistPlays.ToString("N0"),
            TrackAmount = m.MostPlayedTrackPlays,
            TrackAmountText = m.MostPlayedTrackPlays.ToString("N0"),
            Total = m.PlayCount,
            TotalText = Plural.Of(m.PlayCount, "play"),
        }
        : new MonthRow
        {
            Month = m.Month,
            TopArtist = m.TopArtist,
            TopTrack = m.TopTrack,
            TopTrackArtist = m.TopTrackArtist,
            ArtistAmount = m.TopArtistMs,
            ArtistAmountText = m.TopArtistHours.ToString("N1"),
            TrackAmount = m.TopTrackMs,
            TrackAmountText = m.TopTrackHours.ToString("N1"),
            Total = m.TotalMsPlayed,
            TotalText = TimeFormat.HhMmSs(m.TotalTime),
        };
}

/// <summary>
/// One row of a donut's legend: the rank sits in its own column beside the name (blank for
/// "Other"), with the slice's colour as a swatch.
/// </summary>
public sealed record LegendEntry(string Rank, string Name, string Share, Brush Swatch)
{
    public static IReadOnlyList<LegendEntry> For(IEnumerable<ChartBuilder.ShareSlice> slices) =>
        slices.Select(From).ToList();

    public static LegendEntry From(ChartBuilder.ShareSlice slice)
    {
        var brush = new SolidColorBrush(Color.FromRgb(slice.Color.Red, slice.Color.Green, slice.Color.Blue));
        brush.Freeze();
        return new LegendEntry(slice.Rank?.ToString() ?? string.Empty, slice.Name, $"{slice.Percent:0.#}%", brush);
    }
}

/// <summary>
/// One match in the jump box: what it is, how it reads, and the row it stands for, which is
/// what the detail view opens.
/// </summary>
public sealed record JumpResult(string Kind, string Name, string Secondary, string Time, object Item)
{
    public override string ToString() => $"{Kind}: {Name}";
}
