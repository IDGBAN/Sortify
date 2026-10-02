using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
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
    private readonly HistoryParser _parser = new();
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _debounce;
    private List<PlayRecord> _rawRecords = new();
    private AnalysisResult _result = AnalysisResult.Empty;

    /// <summary>The filters <see cref="_result"/> was computed under, as the chip row words them.</summary>
    private IReadOnlyList<string> _resultFilters = Array.Empty<string>();
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
    /// <summary>Each month's top artist and track, newest month first.</summary>
    [ObservableProperty] private IReadOnlyList<MonthStat> _months = Array.Empty<MonthStat>();
    [ObservableProperty] private string _monthlyLeaderText = string.Empty;
    [ObservableProperty] private IReadOnlyList<Milestone> _milestones = Array.Empty<Milestone>();
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

    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isProgressIndeterminate = true;

    /// <summary>Filter sidebar visibility, restored from and saved to settings.</summary>
    [ObservableProperty] private bool _sidebarVisible = true;

    /// <summary>The filters currently narrowing the results, each removable on its own.</summary>
    public ObservableCollection<FilterChip> ActiveFilters { get; } = new();

    [ObservableProperty] private bool _hasActiveFilters;

    /// <summary>Folders opened previously, newest first; bound to the Open Recent menu.</summary>
    public ObservableCollection<string> RecentFolders { get; } = new();

    [ObservableProperty] private string _themeGlyph = "";
    [ObservableProperty] private string _themeTooltip = "Switch to the light theme";

    // Summary (overview) ------------------------------------------------------------------
    [ObservableProperty] private string _totalTimeText = "-";
    [ObservableProperty] private string _totalPlaysText = "-";
    [ObservableProperty] private string _uniqueArtistsText = "-";
    [ObservableProperty] private string _uniqueTracksText = "-";
    [ObservableProperty] private string _dateRangeText = "-";

    // Insights ------------------------------------------------------------------------------
    [ObservableProperty] private string _longestStreakText = "-";
    [ObservableProperty] private string _currentStreakText = "-";
    [ObservableProperty] private string _longestBreakText = "-";
    [ObservableProperty] private string _biggestDayText = "-";
    [ObservableProperty] private string _activeDaysText = "-";
    [ObservableProperty] private string _avgPerDayText = "-";
    [ObservableProperty] private string _skipRateText = "-";
    [ObservableProperty] private string _completionRateText = "-";
    [ObservableProperty] private string _repeatRateText = "-";
    [ObservableProperty] private string _discoveryRateText = "-";
    [ObservableProperty] private string _topArtistShareText = "-";
    [ObservableProperty] private string _peakHourText = "-";
    [ObservableProperty] private string _sessionsText = "-";
    [ObservableProperty] private string _avgSessionText = "-";
    [ObservableProperty] private string _longestSessionText = "-";
    [ObservableProperty] private string _weekSplitText = "-";
    [ObservableProperty] private string _timeOfDayText = "-";

    // Playback context ---------------------------------------------------------------------
    [ObservableProperty] private string _shuffleRateText = "-";
    [ObservableProperty] private string _offlineRateText = "-";
    [ObservableProperty] private string _privateSessionText = "-";
    [ObservableProperty] private string _topDeviceText = "-";

    // Podcasts -----------------------------------------------------------------------------
    [ObservableProperty] private string _podcastTimeText = "-";
    [ObservableProperty] private string _podcastPlaysText = "-";
    [ObservableProperty] private string _uniqueShowsText = "-";
    [ObservableProperty] private string _uniqueEpisodesText = "-";
    [ObservableProperty] private bool _hasPodcastData;

    // Charts ------------------------------------------------------------------------------
    [ObservableProperty] private ChartData _tracksByTime = ChartData.Empty;
    [ObservableProperty] private ChartData _tracksByCount = ChartData.Empty;
    [ObservableProperty] private ChartData _artistsByTime = ChartData.Empty;
    [ObservableProperty] private ChartData _artistsByCount = ChartData.Empty;
    [ObservableProperty] private ChartData _albumsByTime = ChartData.Empty;
    [ObservableProperty] private ChartData _albumsByCount = ChartData.Empty;
    [ObservableProperty] private ChartData _skipped = ChartData.Empty;
    [ObservableProperty] private ChartData _byHour = ChartData.Empty;
    [ObservableProperty] private ChartData _byDayOfWeek = ChartData.Empty;
    [ObservableProperty] private ChartData _heat = ChartData.Empty;
    [ObservableProperty] private ChartData _overTime = ChartData.Empty;
    [ObservableProperty] private ChartData _byYear = ChartData.Empty;
    [ObservableProperty] private ChartData _discovery = ChartData.Empty;
    [ObservableProperty] private ChartData _showsChart = ChartData.Empty;
    [ObservableProperty] private ChartData _episodesChart = ChartData.Empty;

    [ObservableProperty] private ISeries[] _artistShareSeries = Array.Empty<ISeries>();
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
        SidebarVisible = _settings.SidebarVisible;
        ChartAnimationSpeed = _settings.AnimateCharts ? ChartAnimationDuration : TimeSpan.Zero;
        RefreshRecentFolders();
        RefreshThemeButton();

        // Restored before anything listens for changes, so it doesn't trigger a save or a recompute.
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

        // Charts bake their colours into Skia paints, so they have to be rebuilt rather
        // than repainted when the palette changes.
        ThemeService.Changed += (_, _) =>
        {
            RefreshThemeButton();
            if (HasData) UpdateCharts();
        };

        RefreshActiveFilters();
    }

    // ---- Loading ---------------------------------------------------------------------------

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
        var files = dialog.FileNames
            .SelectMany(f => ArchivePath.IsArchive(f) ? HistoryParser.FindHistoryFiles(f) : new[] { f })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
        {
            SetStatus("No Spotify history was found in that ZIP. If it is the file Spotify sent, " +
                      "it may be damaged; try downloading it again.", isError: true);
            return;
        }

        // A lone ZIP goes into Recent the way a folder does.
        string? archive = dialog.FileNames is [var only] && ArchivePath.IsArchive(only) ? only : null;
        await LoadFilesAsync(files, archive);
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

        var files = HistoryParser.FindHistoryFiles(folder);
        if (files.Count == 0)
        {
            SetStatus(isArchive
                ? "No Spotify history was found in that ZIP. If it is the file Spotify sent, " +
                  "it may be damaged; try downloading it again."
                : "No Spotify history JSON files were found in that folder.", isError: true);
            return;
        }

        await LoadFilesAsync(files, folder);
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

        await LoadFilesAsync(files, _settings.LastFolder);
    }

    /// <summary>Re-reads the files behind the current results, bypassing the cache.</summary>
    [RelayCommand(CanExecute = nameof(HasData))]
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
    /// Parses the given history files and runs analysis. Also used by drag &amp; drop.
    /// <paramref name="folder"/> is the folder or ZIP they came from, remembered in Recent.
    /// </summary>
    public async Task LoadFilesAsync(IReadOnlyList<string> filePaths, string? folder = null)
    {
        if (filePaths.Count == 0 || _isLoading)
            return;

        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        _isLoading = true;
        RefreshBusy();
        IsProgressIndeterminate = true;
        ProgressValue = 0;

        // Reloading the same export keeps the artists being compared; a different one starts
        // again from its own top artists.
        if (!filePaths.SequenceEqual(_loadedFiles, StringComparer.OrdinalIgnoreCase))
            _comparisonSeeded = false;

        try
        {
            _loadedFolder = folder;
            if (folder is not null)
            {
                _settings.RememberFolder(folder);
                _settings.Save();
                RefreshRecentFolders();
            }

            // Re-reading an unchanged export is the common case (relaunching the app, or
            // reopening the same folder), and parsing it again costs seconds for nothing.
            SetStatus("Checking for cached results...");
            var cached = await Task.Run(() => RecordCache.TryLoad(filePaths), token);
            if (cached is { Count: > 0 })
            {
                _rawRecords = cached.ToList();
                _loadedFiles = filePaths;
                OfferChoicesForHistory();
                SetStatus($"Loaded {_rawRecords.Count:N0} plays from cache. Crunching numbers...");
                HasData = true;
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

            var parsed = await _parser.ParseAsync(filePaths, progress, token);
            _rawRecords = parsed.Records;
            _loadedFiles = filePaths;

            if (_rawRecords.Count == 0)
            {
                HasData = false;
                SetStatus(parsed.Warnings.Count > 0
                    ? $"No valid listening data found. {parsed.Warnings[0]}"
                    : "No valid listening data found in the selected files.", isError: true);
                return;
            }

            OfferChoicesForHistory();
            int problems = parsed.Warnings.Count + parsed.SkippedFiles.Count;
            string warn = problems > 0 ? $" ({problems} file(s) skipped)" : string.Empty;
            IsProgressIndeterminate = true;
            SetStatus($"Loaded {_rawRecords.Count:N0} plays from {filePaths.Count} file(s){warn}. Crunching numbers...");
            HasData = true;
            await RecomputeAsync();

            // Said after the analysis rather than before it, which would overwrite it at once.
            if (parsed.DuplicatesRemoved > 0 && !StatusIsError)
            {
                SetStatus($"{StatusText} Left out {parsed.DuplicatesRemoved:N0} " +
                          $"play{(parsed.DuplicatesRemoved == 1 ? "" : "s")} that more than one of the loaded files held.");
            }

            // Save after analysis so the user isn't waiting on disk I/O to see results.
            var toCache = _rawRecords;
            _ = Task.Run(() => RecordCache.TrySave(filePaths, toCache), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Loading cancelled.");
        }
        finally
        {
            _isLoading = false;
            RefreshBusy();
            IsProgressIndeterminate = true;
            ProgressValue = 0;
        }
    }

    private void RefreshBusy() => IsBusy = _isLoading || _pendingAnalyses > 0;

    /// <summary>
    /// Last dated play in the whole loaded history, whatever the filters say. The quick date
    /// ranges count back from it.
    /// </summary>
    internal DateTime? LastListenInHistory { get; private set; }

    /// <summary>Calendar years the loaded history has plays in, oldest first.</summary>
    internal IReadOnlyList<int> YearsInHistory { get; private set; } = Array.Empty<int>();

    /// <summary>
    /// Offers the quick date ranges, devices and countries that fit the history just loaded.
    /// Read off the raw records rather than the filtered results, so filtering to one year
    /// or one device doesn't take the other choices away.
    /// </summary>
    private void OfferChoicesForHistory()
    {
        DateTime? last = null;
        var years = new SortedSet<int>();
        var platformMs = new Dictionary<string, long>(StringComparer.Ordinal);
        var countryMs = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in _rawRecords)
        {
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

        LastListenInHistory = last;
        YearsInHistory = years.ToList();
        Filters.SetAvailableDates(last, years);

        var devices = platformMs
            .GroupBy(kv => AnalysisEngine.PlatformFamily(kv.Key))
            .OrderByDescending(g => g.Sum(kv => kv.Value))
            .Select(g => (g.Key, g.Key, g.Key));
        var countries = countryMs
            .OrderByDescending(kv => kv.Value)
            .Select(kv => (kv.Key, kv.Key.ToUpperInvariant(), CountryName(kv.Key)));
        Filters.SetAvailablePlayback(devices, countries);
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
            OverTime = ChartBuilder.OverTime(_result, granularity);
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
        RefreshRecentFolders();
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

    public void ExcludeArtistFromGrid(string artist) => Filters.ExcludeArtist(artist);
    public void ExcludeTrackFromGrid(string track) => Filters.ExcludeTrack(track);
    public void ExcludeArtistsFromGrid(IEnumerable<string> artists) => Filters.ExcludeArtists(artists);
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
            : $" {links.Missing:N0} track{(links.Missing == 1 ? " has" : "s have")} no link in the export and {(links.Missing == 1 ? "was" : "were")} left out.";
        SetStatus($"Copied {links.Copied:N0} Spotify link{(links.Copied == 1 ? "" : "s")}. " +
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

        return await DetailEngine.BuildAsync(_rawRecords, Filters.ToOptions(), scope, title, subtitle);
    }

    // ---- Analysis --------------------------------------------------------------------------

    private async Task RecomputeAsync()
    {
        if (!HasData) return;

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
            AnalysisResult result;
            try
            {
                result = await AnalysisEngine.AnalyzeAsync(_rawRecords, options, _settings.SessionGap, token);
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

            UpdateCollections();
            UpdateSummary();
            UpdateInsights();
            UpdateCharts();
            NotifyExportsChanged();
            if (IsJumpOpen)
                RefreshJump();

            SetStatus(_result.TotalPlays == 0
                ? Filters.HasInvalidDateRange
                    ? "No plays match: the From date is after the To date."
                    : "No plays match the current filters."
                : $"Showing {_result.TotalPlays:N0} plays across {_result.UniqueTracks:N0} tracks, " +
                  $"{_result.UniqueArtists:N0} artists and {_result.UniqueAlbums:N0} albums.");

            SeedComparison();
            RefreshCompareSuggestions();
            await RefreshArtistTimelineAsync();
        }
        finally
        {
            _pendingAnalyses--;
            RefreshBusy();
        }
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
        Months = _result.Months.Reverse().ToList();
        MonthlyLeaderText = DescribeMonthlyLeader(_result.Months);
        Milestones = _result.Milestones;
        HasPodcastData = _result.Shows.Count > 0;

        TopTracks = _result.Tracks.Take(5)
            .Select((t, i) => new RankedItem(i + 1, t.Track, t.Artist, TimeFormat.Friendly(t.TotalTime), t.PlayCount))
            .ToList();
        TopArtists = _result.Artists.Take(5)
            .Select((a, i) => new RankedItem(i + 1, a.Artist, string.Empty, TimeFormat.Friendly(a.TotalTime), a.PlayCount))
            .ToList();
        TopAlbums = _result.Albums.Take(5)
            .Select((a, i) => new RankedItem(i + 1, a.Album, a.Artist, TimeFormat.Friendly(a.TotalTime), a.PlayCount))
            .ToList();
    }

    // ---- Quick filters ------------------------------------------------------------------------

    partial void OnTrackQuickFilterChanged(string value) => ApplyTrackQuickFilter();
    partial void OnArtistQuickFilterChanged(string value) => ApplyArtistQuickFilter();
    partial void OnAlbumQuickFilterChanged(string value) => ApplyAlbumQuickFilter();
    partial void OnShowQuickFilterChanged(string value) => ApplyShowQuickFilter();

    private void ApplyTrackQuickFilter() =>
        Tracks = Narrow(_result.Tracks, TrackQuickFilter, t => t.Track, t => t.Artist);

    private void ApplyArtistQuickFilter() =>
        Artists = Narrow(_result.Artists, ArtistQuickFilter, a => a.Artist);

    private void ApplyAlbumQuickFilter() =>
        Albums = Narrow(_result.Albums, AlbumQuickFilter, a => a.Album, a => a.Artist);

    private void ApplyShowQuickFilter()
    {
        Shows = Narrow(_result.Shows, ShowQuickFilter, s => s.Show);
        Episodes = Narrow(_result.Episodes, ShowQuickFilter, e => e.Episode, e => e.Show);
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

    // ---- Comparing artists over time ------------------------------------------------------------

    /// <summary>How many of the top artists the comparison starts with after a history loads.</summary>
    private const int ComparedArtistsToStartWith = 3;

    /// <summary>How many name suggestions show under the box while typing.</summary>
    private const int CompareSuggestionCount = 6;

    /// <summary>Artists on the comparison chart, in the order their lines are coloured.</summary>
    public ObservableCollection<string> ComparedArtists { get; } = new();

    [ObservableProperty] private string _compareQuery = string.Empty;
    [ObservableProperty] private IReadOnlyList<string> _compareSuggestions = Array.Empty<string>();
    [ObservableProperty] private ChartData _artistTimelineChart = ChartData.Empty;
    [ObservableProperty] private string _compareHint = string.Empty;

    private ArtistTimeline _artistTimeline = ArtistTimeline.Empty;
    private ChartBuilder.TimeGranularity _compareGranularity = ChartBuilder.TimeGranularity.Monthly;
    private CancellationTokenSource? _timelineCts;

    /// <summary>
    /// The comparison starts out with the top few artists, once per loaded history, so the
    /// chart isn't blank. After that it only changes when the user changes it.
    /// </summary>
    private bool _comparisonSeeded;

    partial void OnCompareQueryChanged(string value) => RefreshCompareSuggestions();

    private void RefreshCompareSuggestions()
    {
        string query = CompareQuery.Trim();
        CompareSuggestions = query.Length == 0
            ? Array.Empty<string>()
            : _result.Artists
                .Where(a => !ComparedArtists.Contains(a.Artist))
                .Select(a => (a.Artist, Rank: MatchRank(a.Artist, query), a.TotalMsPlayed))
                .Where(x => x.Rank >= 0)
                .OrderBy(x => x.Rank)
                .ThenByDescending(x => x.TotalMsPlayed)
                .Take(CompareSuggestionCount)
                .Select(x => x.Artist)
                .ToList();
        RefreshCompareHint();
    }

    private void RefreshCompareHint()
    {
        CompareHint = ComparedArtists.Count == 0
            ? $"Type an artist's name and pick from the matches to add them. Up to {ArtistComparison.MaxArtists} at once."
            : CompareQuery.Trim().Length > 0 && CompareSuggestions.Count == 0
                ? ComparedArtists.Count >= ArtistComparison.MaxArtists
                    ? $"The chart already has {ArtistComparison.MaxArtists} artists. Remove one to add another."
                    : $"No artist in the current results matches “{CompareQuery.Trim()}”."
                : string.Empty;
    }

    [RelayCommand]
    private async Task AddComparedArtistAsync(string? artist)
    {
        artist ??= CompareSuggestions.FirstOrDefault();
        if (string.IsNullOrEmpty(artist) || ComparedArtists.Contains(artist))
            return;
        if (ComparedArtists.Count >= ArtistComparison.MaxArtists)
        {
            RefreshCompareHint();
            SetStatus($"The comparison chart holds {ArtistComparison.MaxArtists} artists at most. Remove one to add {artist}.");
            return;
        }

        ComparedArtists.Add(artist);
        CompareQuery = string.Empty;
        await RefreshArtistTimelineAsync();
    }

    [RelayCommand]
    private async Task RemoveComparedArtistAsync(string? artist)
    {
        if (artist is null || !ComparedArtists.Remove(artist))
            return;
        RefreshCompareSuggestions();
        await RefreshArtistTimelineAsync();
    }

    /// <summary>Switches the comparison between monthly and weekly buckets.</summary>
    public async Task SetCompareGranularityAsync(ChartBuilder.TimeGranularity granularity)
    {
        if (_compareGranularity == granularity)
            return;
        _compareGranularity = granularity;
        await RefreshArtistTimelineAsync();
    }

    private void SeedComparison()
    {
        if (_comparisonSeeded)
            return;
        _comparisonSeeded = true;
        ComparedArtists.Clear();
        foreach (var artist in _result.Artists.Take(ComparedArtistsToStartWith))
            ComparedArtists.Add(artist.Artist);
    }

    /// <summary>Rebuilds the comparison under the current filters; a newer call cancels an older one.</summary>
    private async Task RefreshArtistTimelineAsync()
    {
        RefreshCompareHint();
        var previous = _timelineCts;
        _timelineCts = new CancellationTokenSource();
        previous?.Cancel();
        var token = _timelineCts.Token;

        try
        {
            var timeline = HasData && ComparedArtists.Count > 0
                ? await ArtistComparison.BuildAsync(_rawRecords, Filters.ToOptions(), ComparedArtists.ToList(),
                    _compareGranularity, token)
                : ArtistTimeline.Empty;
            if (token.IsCancellationRequested)
                return;
            _artistTimeline = timeline;
            ArtistTimelineChart = ChartBuilder.ArtistTimelines(_artistTimeline, _compareGranularity);
        }
        catch (OperationCanceledException)
        {
            // A newer refresh took over; it sets the chart.
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
            .Select(a => new JumpResult("Artist", a.Artist, $"{a.PlayCount:N0} plays", TimeFormat.Friendly(a.TotalTime), a)));
        results.AddRange(Best(r.Tracks, t => t.Track, t => t.TotalMsPlayed)
            .Select(t => new JumpResult("Track", t.Track, t.Artist, TimeFormat.Friendly(t.TotalTime), t)));
        results.AddRange(Best(r.Albums, a => a.Album, a => a.TotalMsPlayed)
            .Select(a => new JumpResult("Album", a.Album, a.Artist, TimeFormat.Friendly(a.TotalTime), a)));
        results.AddRange(Best(r.Shows, s => s.Show, s => s.TotalMsPlayed)
            .Select(s => new JumpResult(s.Kind == ContentKind.Audiobook ? "Audiobook" : "Podcast", s.Show,
                $"{s.EpisodeCount:N0} episode{(s.EpisodeCount == 1 ? "" : "s")}", TimeFormat.Friendly(s.TotalTime), s)));
        results.AddRange(Best(r.Episodes, e => e.Episode, e => e.TotalMsPlayed)
            .Select(e => new JumpResult("Episode", e.Episode, e.Show, TimeFormat.Friendly(e.TotalTime), e)));
        return results;

        IEnumerable<T> Best<T>(IReadOnlyList<T> items, Func<T, string> name, Func<T, long> ms) => items
            .Select(item => (Item: item, Rank: MatchRank(name(item), query)))
            .Where(x => x.Rank >= 0)
            .OrderBy(x => x.Rank)
            .ThenByDescending(x => ms(x.Item))
            .Take(perKind)
            .Select(x => x.Item);
    }

    /// <summary>0 for the whole name, 1 for its start, 2 for the start of a later word, 3 for anywhere else, -1 for no match.</summary>
    internal static int MatchRank(string name, string query)
    {
        int at = name.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return -1;
        if (at == 0)
            return name.Length == query.Length ? 0 : 1;

        for (; at > 0; at = name.IndexOf(query, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (!char.IsLetterOrDigit(name[at - 1]))
                return 2;
        }
        return 3;
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

        LongestBreakText = r.LongestBreakDays > 0 && r.LongestBreakStart is { } bs && r.LongestBreakEnd is { } be
            ? $"{r.LongestBreakDays} day{(r.LongestBreakDays == 1 ? "" : "s")}  ({bs:yyyy-MM-dd} to {be:yyyy-MM-dd})"
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

        CompletionRateText = r.SkipEligiblePlays > 0
            ? $"{r.CompletedPlays * 100.0 / r.SkipEligiblePlays:0.#}%  ({r.CompletedPlays:N0} plays)"
            : "-";

        RepeatRateText = r.UniqueTracks > 0
            ? $"{r.PlaysPerTrack:0.0} plays per track"
            : "-";

        DiscoveryRateText = r.NewArtistsByMonth.Count > 0
            ? $"{r.NewArtistsPerMonth:0.#} new artists / month"
            : "-";

        TopArtistShareText = r.Artists.Count > 0
            ? $"{r.Artists[0].Artist}  ({r.TopArtistSharePercent:0.#}% of your time)"
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

        PrivateSessionText = r.ShuffleEligiblePlays > 0
            ? $"{r.IncognitoPlays * 100.0 / r.ShuffleEligiblePlays:0.#}%  ({r.IncognitoPlays:N0} plays)"
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

    /// <summary>
    /// Who topped the most months, and their longest run of back-to-back months on top. Ties
    /// go to the artist with more listening in the months they led.
    /// </summary>
    internal static string DescribeMonthlyLeader(IReadOnlyList<MonthStat> months)
    {
        if (months.Count == 0)
            return string.Empty;

        var leader = months
            .GroupBy(m => m.TopArtist)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Sum(m => m.TopArtistMs))
            .First();

        int best = 0, run = 0;
        DateTime? previous = null;
        DateTime runStart = default, bestStart = default, bestEnd = default;
        foreach (var month in months.Where(m => m.TopArtist == leader.Key))
        {
            run = previous is { } p && p.AddMonths(1) == month.Month ? run + 1 : 1;
            if (run == 1)
                runStart = month.Month;
            if (run > best)
            {
                best = run;
                bestStart = runStart;
                bestEnd = month.Month;
            }
            previous = month.Month;
        }

        int count = leader.Count();
        string text = $"{leader.Key} was your #1 artist in {count} of {months.Count} month{(months.Count == 1 ? "" : "s")}";
        return best > 1
            ? $"{text}, {best} of them in a row ({bestStart:yyyy-MM} to {bestEnd:yyyy-MM})."
            : text + ".";
    }

    /// <summary>The busiest hour of the week, read off the same day-by-hour grid as the heatmap.</summary>
    internal static string BuildPeakHourText(AnalysisResult r)
    {
        int peakDow = 0, peakHour = 0;
        for (int d = 0; d < 7; d++)
        {
            for (int h = 0; h < 24; h++)
            {
                if (r.PlaytimeByDowHour[d, h] > r.PlaytimeByDowHour[peakDow, peakHour])
                    (peakDow, peakHour) = (d, h);
            }
        }

        // Undated plays count toward the totals but never reach the grid.
        if (r.PlaytimeByDowHour[peakDow, peakHour] == 0)
            return "-";

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

    // ---- Charts ------------------------------------------------------------------------------

    private void UpdateCharts()
    {
        ByHour = ChartBuilder.ByHour(_result);
        ByDayOfWeek = ChartBuilder.ByDayOfWeek(_result);
        Heat = ChartBuilder.DowHourHeat(_result);
        OverTime = ChartBuilder.OverTime(_result, _overTimeGranularity);
        ByYear = ChartBuilder.HoursPerYear(_result);
        Discovery = ChartBuilder.NewArtistsByMonth(_result);
        Skipped = ChartBuilder.TopSkippedTracks(_result);
        ArtistShareSeries = ChartBuilder.ArtistShare(_result);
        ReasonEndSeries = ChartBuilder.ReasonEndShare(_result);
        PlatformSeries = ChartBuilder.PlatformShare(_result);
        CountrySeries = ChartBuilder.CountryShare(_result);

        // Size these to their content: most libraries hold only a handful of shows, and a
        // fixed-height chart would space three bars across half a screen.
        ShowsChart = ChartBuilder.TopShows(_result);
        EpisodesChart = ChartBuilder.TopEpisodes(_result);
        ShowsChartHeight = BarHeight(Math.Min(ChartBuilder.PodcastBars, _result.Shows.Count));
        EpisodesChartHeight = BarHeight(Math.Min(ChartBuilder.PodcastBars, _result.Episodes.Count));
        ArtistTimelineChart = ChartBuilder.ArtistTimelines(_artistTimeline, _compareGranularity);

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
        TracksByTime = ChartBuilder.TopTracksByTime(_result, _tracksShown);
        TracksByCount = ChartBuilder.TopTracksByCount(_result, _tracksShown);
        TracksChartHeight = BarHeight(_tracksShown);
    }

    private void BuildArtistCharts()
    {
        ArtistsByTime = ChartBuilder.TopArtistsByTime(_result, _artistsShown);
        ArtistsByCount = ChartBuilder.TopArtistsByCount(_result, _artistsShown);
        ArtistsChartHeight = BarHeight(_artistsShown);
    }

    private void BuildAlbumCharts()
    {
        AlbumsByTime = ChartBuilder.TopAlbumsByTime(_result, _albumsShown);
        AlbumsByCount = ChartBuilder.TopAlbumsByCount(_result, _albumsShown);
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
    }

    /// <summary>Sets the status line, and whether it should be shown as a problem.</summary>
    public void SetStatus(string text, bool isError = false)
    {
        StatusText = text;
        StatusIsError = isError;
    }
}

/// <summary>One row of an Overview top-five list.</summary>
public sealed record RankedItem(int Rank, string Name, string Secondary, string Time, int Plays);

/// <summary>
/// One match in the jump box: what it is, how it reads, and the row it stands for, which is
/// what the detail view opens.
/// </summary>
public sealed record JumpResult(string Kind, string Name, string Secondary, string Time, object Item)
{
    public override string ToString() => $"{Kind}: {Name}";
}
