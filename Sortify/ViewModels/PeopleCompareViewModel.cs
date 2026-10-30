using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Sortify.Models;
using Sortify.Services;

namespace Sortify.ViewModels;

/// <summary>
/// The Compare People tab: your history against someone else's export, under the sidebar's
/// filters. Their plays are kept in memory only, never in the record cache or the recent list,
/// and are dropped when the app closes. Like the Compare Periods tab, it only works anything
/// out while it is on screen.
/// </summary>
public sealed partial class PeopleCompareViewModel : ObservableObject
{
    private const string DefaultName = "Them";

    private readonly Func<CompareViewModel.Source?> _source;
    private CancellationTokenSource? _cts;
    private bool _stale = true;
    private IReadOnlyList<PlayRecord> _theirRecords = Array.Empty<PlayRecord>();

    /// <summary>Their plays with re-releases folded in, worked out the first time the sidebar asks for it.</summary>
    private IReadOnlyList<PlayRecord>? _theirCombined;

    private CancellationTokenSource? _loadCts;

    // Where their plays came from and how their times were converted, to read them again
    // when the time zone setting changes.
    private Func<IReadOnlyList<string>>? _theirFiles;
    private string? _theirTimeZones;

    public PeopleCompareViewModel(Func<CompareViewModel.Source?> source) => _source = source;

    /// <summary>What the other person is called on this tab. Editable; blank reads as "Them".</summary>
    [ObservableProperty] private string _theirName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private bool _hasTheirs;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenTheirFolderCommand), nameof(OpenTheirFilesCommand), nameof(CancelLoadCommand))]
    private bool _isLoading;

    /// <summary>How far through their files a load has got, 0 to 100.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoadProgressUnknown))]
    private double _loadProgress;

    /// <summary>True until a file has been read: one big file gives no progress until it is done.</summary>
    public bool IsLoadProgressUnknown => LoadProgress <= 0;

    [ObservableProperty] private string _loadStatus = string.Empty;

    [ObservableProperty] private string _loadedText = string.Empty;
    [ObservableProperty] private string _loadError = string.Empty;

    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _hasResults;

    [ObservableProperty] private string _artistMatch = "-";
    [ObservableProperty] private string _trackMatch = "-";
    [ObservableProperty] private string _sharedArtistCount = "-";
    [ObservableProperty] private string _sharedTrackCount = "-";

    [ObservableProperty] private IReadOnlyList<ComparisonMetric> _metrics = Array.Empty<ComparisonMetric>();
    [ObservableProperty] private IReadOnlyList<ComparisonRow> _sharedArtists = Array.Empty<ComparisonRow>();
    [ObservableProperty] private IReadOnlyList<ComparisonRow> _sharedTracks = Array.Empty<ComparisonRow>();
    [ObservableProperty] private IReadOnlyList<ComparisonRow> _onlyYou = Array.Empty<ComparisonRow>();
    [ObservableProperty] private IReadOnlyList<ComparisonRow> _onlyThem = Array.Empty<ComparisonRow>();

    /// <summary>Set by the window as the Compare People tab comes and goes.</summary>
    [ObservableProperty] private bool _isVisible;

    /// <summary>
    /// What the tables rank by and the match figures measure; follows the main window's Time /
    /// Plays switch. Changing it re-ranks the last comparison rather than analysing both again.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnlyYouHint), nameof(OnlyThemHint), nameof(SharedHint),
        nameof(ArtistMatchHint), nameof(TrackMatchHint))]
    private Metric _metric;

    /// <summary>The two histories last analysed, kept so a change of metric only has to re-rank them.</summary>
    private PeopleComparisonResult? _last;

    partial void OnMetricChanged(Metric value)
    {
        if (_last is { } last)
            Show(PeopleComparison.Build(last.You, last.Them, value));
    }

    /// <summary>The other person's name as headings use it.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(TheirName) ? DefaultName : TheirName.Trim();

    private string MostPlayedFirst => Metric == Metric.Plays ? ", most played first" : string.Empty;

    public string OnlyYouTitle => $"Only You, Not {DisplayName}";
    public string OnlyThemTitle => $"Only {DisplayName}, Not You";
    public string OnlyYouHint => $"Artists you listened to for at least an hour that {DisplayName} never played{MostPlayedFirst}.";
    public string OnlyThemHint => $"Artists {DisplayName} listened to for at least an hour that you never played{MostPlayedFirst}.";

    public string SharedHint => Metric == Metric.Plays
        ? "Artists you both play, the ones you're most alike on first, with where each of you ranks them by plays. Double-click a row for your breakdown."
        : "Artists you both play, the ones you're most alike on first, with where each of you ranks them by listening time. Double-click a row for your breakdown.";

    public string ArtistMatchHint => Metric == Metric.Plays
        ? "How many of your plays and theirs go to the same artists"
        : "How much of your listening time and theirs goes to the same artists";

    public string TrackMatchHint => Metric == Metric.Plays
        ? "How many of your plays and theirs go to the same tracks"
        : "How much of your listening time and theirs goes to the same tracks";

    partial void OnTheirNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(OnlyYouTitle));
        OnPropertyChanged(nameof(OnlyThemTitle));
        OnPropertyChanged(nameof(OnlyYouHint));
        OnPropertyChanged(nameof(OnlyThemHint));
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (value && _stale)
            _ = RefreshAsync();
    }

    /// <summary>Your history or the sidebar changed, so the comparison is out of date.</summary>
    public void Invalidate()
    {
        _stale = true;
        if (IsVisible)
            _ = RefreshAsync();
    }

    private bool CanOpen() => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenTheirFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the folder with the other person's Spotify streaming history",
        };
        if (dialog.ShowDialog() != true)
            return;

        string folder = dialog.FolderName;
        await LoadTheirsAsync(() => HistoryParser.FindHistoryFiles(folder), NameFrom(folder));
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenTheirFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the other person's Spotify history: their export ZIP or its JSON files",
            Filter = "Spotify history (*.json, *.zip)|*.json;*.zip|JSON files (*.json)|*.json|" +
                     "ZIP files (*.zip)|*.zip|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true)
            return;

        var picked = dialog.FileNames;
        IReadOnlyList<string> FindFiles() => picked
            .SelectMany(f => ArchivePath.IsArchive(f) ? HistoryParser.FindHistoryFiles(f) : new[] { f })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        await LoadTheirsAsync(FindFiles, null);
    }

    [RelayCommand(CanExecute = nameof(HasTheirs))]
    private void Remove()
    {
        _cts?.Cancel();
        _theirRecords = Array.Empty<PlayRecord>();
        _theirCombined = null;
        _theirFiles = null;
        HasTheirs = false;
        LoadedText = string.Empty;
        LoadError = string.Empty;
        Message = string.Empty;
        IsWorking = false;
        ClearResults();
        _stale = true;
    }

    /// <summary>
    /// The export's folder or ZIP name is rarely a person's name ("my_spotify_data"), so a
    /// name is only suggested when nothing has been typed yet and the folder isn't one of
    /// Spotify's own.
    /// </summary>
    internal static string? NameFrom(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/'));
        return name.Length == 0
            || name.Contains("spotify", StringComparison.OrdinalIgnoreCase)
            || name.Contains("history", StringComparison.OrdinalIgnoreCase)
            ? null
            : name;
    }

    /// <summary>
    /// Finds and parses the other person's history files off the UI thread, converting play
    /// times the same way as your own. A load that finds no plays, finds your own export or is
    /// cancelled leaves whatever was loaded before in place.
    /// </summary>
    internal async Task LoadTheirsAsync(Func<IReadOnlyList<string>> findFiles, string? suggestedName)
    {
        if (IsLoading)
            return;

        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading = true;
        LoadError = string.Empty;
        LoadProgress = 0;
        LoadStatus = "Looking for their Spotify history...";
        try
        {
            var files = await Task.Run(findFiles, token);
            token.ThrowIfCancellationRequested();
            if (files.Count == 0)
            {
                LoadError = "No Spotify history was found there. Pick the ZIP Spotify sent, the folder it unpacks to, or its JSON files.";
                return;
            }

            if (_source()?.Files is { Count: > 0 } yours && yours.SequenceEqual(files, StringComparer.OrdinalIgnoreCase))
            {
                LoadError = "That's the export already open as yours. Pick the other person's.";
                return;
            }

            var progress = new Progress<ParseProgress>(p =>
            {
                LoadProgress = p.Percent;
                LoadStatus = p.Message;
            });
            var timeZones = _source()?.TimeZones;
            var parsed = await new HistoryParser(timeZones).ParseAsync(files, progress, token);
            if (parsed.Records.Count == 0)
            {
                LoadError = parsed.Warnings.Count > 0
                    ? $"No valid listening data found. {parsed.Warnings[0]}"
                    : "No valid listening data found in those files.";
                return;
            }

            SetTheirs(parsed.Records, suggestedName);
            _theirFiles = findFiles;
            _theirTimeZones = (timeZones ?? PlayTimeZones.ThisPc).CacheKey;
            int skipped = parsed.Warnings.Count + parsed.SkippedFiles.Count;
            if (skipped > 0)
                LoadedText += $" ({Plural.Of(skipped, "file")} skipped)";
        }
        catch (OperationCanceledException)
        {
            // Cancelled from the button.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LoadError = $"Couldn't read that history: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            LoadProgress = 0;
        }
    }

    [RelayCommand(CanExecute = nameof(IsLoading))]
    private void CancelLoad() => _loadCts?.Cancel();

    /// <summary>Reads their export again if play times are now converted differently from when it was read.</summary>
    public Task ReadTheirsAgainIfTimesChangedAsync()
    {
        string now = (_source()?.TimeZones ?? PlayTimeZones.ThisPc).CacheKey;
        return HasTheirs && _theirFiles is { } files && now != _theirTimeZones
            ? LoadTheirsAsync(files, null)
            : Task.CompletedTask;
    }

    /// <summary>Makes <paramref name="records"/> the other person's history and compares again.</summary>
    internal void SetTheirs(IReadOnlyList<PlayRecord> records, string? suggestedName = null)
    {
        _theirRecords = records;
        _theirCombined = null;
        HasTheirs = true;
        if (string.IsNullOrWhiteSpace(TheirName) && suggestedName is not null)
            TheirName = suggestedName;

        var (first, last) = PlayRecord.DateSpan(records);
        string plays = Plural.Of(records.Count, "play");
        LoadedText = first is { } f && last is { } l
            ? $"{plays} loaded, {TimeFormat.Day(f)} to {TimeFormat.Day(l)}."
            : $"{plays} loaded.";
        Invalidate();
    }

    public async Task RefreshAsync()
    {
        if (!HasTheirs)
        {
            ClearResults();
            _stale = false;
            return;
        }

        if (_source() is not { } source)
        {
            Message = "Open your own Spotify history first, then their plays can be put next to yours.";
            ClearResults();
            _stale = false;
            return;
        }

        var previous = _cts;
        _cts = new CancellationTokenSource();
        previous?.Cancel();
        var token = _cts.Token;
        var theirs = _theirRecords;

        IsWorking = true;
        try
        {
            // Your plays arrive with re-releases already folded when the sidebar asks for it;
            // theirs need the same, or "Song - Remastered" on one side never meets "Song".
            if (source.CombineReleases)
            {
                var raw = theirs;
                theirs = _theirCombined ?? await Task.Run(() => ReleaseNames.Combine(raw), token);
                if (ReferenceEquals(raw, _theirRecords))
                    _theirCombined = theirs;
            }

            var metric = Metric;
            var result = await PeopleComparison.CompareAsync(source.Records, theirs, source.Filter, metric,
                source.SessionGap, token);
            if (token.IsCancellationRequested)
                return;

            // The switch may have been flipped while both histories were being worked out.
            Show(metric == Metric ? result : PeopleComparison.Build(result.You, result.Them, Metric));
            Message = (result.You.TotalPlays, result.Them.TotalPlays) switch
            {
                (0, 0) => "Neither of you has any plays under the current filters.",
                (0, _) => "You have no plays under the current filters.",
                (_, 0) => $"{DisplayName} has no plays under the current filters.",
                _ => string.Empty,
            };
            _stale = false;
        }
        catch (OperationCanceledException)
        {
            // A newer refresh took over, or their history was removed.
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsWorking = false;
            previous?.Dispose();
        }
    }

    // A sliver of overlap reads as "<1%" rather than rounding away to nothing in common.
    private static string Percent(double value) => value switch
    {
        <= 0 => "0%",
        < 1 => "<1%",
        _ => $"{value:0}%",
    };

    private void Show(PeopleComparisonResult result)
    {
        _last = result;
        Metrics = result.Metrics;
        SharedArtists = result.SharedArtists;
        SharedTracks = result.SharedTracks;
        OnlyYou = result.OnlyYou;
        OnlyThem = result.OnlyThem;
        ArtistMatch = Percent(result.ArtistMatch);
        TrackMatch = Percent(result.TrackMatch);
        SharedArtistCount = result.SharedArtistCount.ToString("N0");
        SharedTrackCount = result.SharedTrackCount.ToString("N0");
        HasResults = true;
    }

    private void ClearResults()
    {
        _last = null;
        Metrics = Array.Empty<ComparisonMetric>();
        SharedArtists = Array.Empty<ComparisonRow>();
        SharedTracks = Array.Empty<ComparisonRow>();
        OnlyYou = Array.Empty<ComparisonRow>();
        OnlyThem = Array.Empty<ComparisonRow>();
        ArtistMatch = TrackMatch = SharedArtistCount = SharedTrackCount = "-";
        HasResults = false;
    }
}
