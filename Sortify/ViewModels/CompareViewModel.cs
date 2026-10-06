using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Sortify.Models;
using Sortify.Services;

namespace Sortify.ViewModels;

/// <summary>
/// The Compare tab: two date ranges of the loaded history side by side, under the sidebar's
/// other filters, or the loaded history next to someone else's. Only works anything out while
/// the tab is on screen; changes made while it is hidden are picked up when it is shown again.
/// </summary>
public sealed partial class CompareViewModel : ObservableObject
{
    /// <summary>
    /// What a comparison runs over: the plays, the sidebar filters, the session gap, the
    /// files the plays were read from (so the same export can't be opened as someone else's)
    /// and how play times are converted, which someone else's export is read with too.
    /// </summary>
    public readonly record struct Source(
        IReadOnlyList<PlayRecord> Records, FilterOptions Filter, TimeSpan SessionGap,
        IReadOnlyList<string>? Files = null, PlayTimeZones? TimeZones = null);

    private readonly Func<Source?> _source;
    private CancellationTokenSource? _cts;
    private bool _stale = true;
    private bool _settingPeriods;

    /// <summary>Someone else's plays, held for the session and never cached or saved.</summary>
    private List<PlayRecord>? _other;
    private CancellationTokenSource? _otherCts;

    // Where their plays came from and how their times were converted, to read them again
    // when the time zone setting changes.
    private Func<IReadOnlyList<string>>? _otherFiles;
    private string _otherName = string.Empty;
    private string? _otherTimeZones;

    public CompareViewModel(Func<Source?> source) => _source = source;

    [ObservableProperty] private DateTime? _startA;
    [ObservableProperty] private DateTime? _endA;
    [ObservableProperty] private DateTime? _startB;
    [ObservableProperty] private DateTime? _endB;

    public ObservableCollection<DateRangeOption> RangesA { get; } = new();
    public ObservableCollection<DateRangeOption> RangesB { get; } = new();

    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string _periodALabel = "Period A";
    [ObservableProperty] private string _periodBLabel = "Period B";

    [ObservableProperty] private IReadOnlyList<ComparisonMetric> _metrics = Array.Empty<ComparisonMetric>();
    [ObservableProperty] private IReadOnlyList<ComparisonRow> _artists = Array.Empty<ComparisonRow>();
    [ObservableProperty] private IReadOnlyList<ComparisonRow> _tracks = Array.Empty<ComparisonRow>();
    [ObservableProperty] private IReadOnlyList<ComparisonRow> _newInB = Array.Empty<ComparisonRow>();
    [ObservableProperty] private IReadOnlyList<ComparisonRow> _goneSinceA = Array.Empty<ComparisonRow>();

    /// <summary>True when the tab sets the loaded history next to someone else's instead of comparing two periods.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPeriodsMode))]
    private bool _isPeopleMode;

    public bool IsPeriodsMode
    {
        get => !IsPeopleMode;
        set => IsPeopleMode = !value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenOther))]
    private bool _hasOther;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenOther))]
    [NotifyCanExecuteChangedFor(nameof(CancelOtherCommand))]
    private bool _isLoadingOther;

    /// <summary>True while there is no other history, open or on its way, so the buttons to open one show.</summary>
    public bool CanOpenOther => !HasOther && !IsLoadingOther;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOtherProgressUnknown))]
    private double _otherProgress;

    /// <summary>True until a file has been read: one big file gives no progress until it is done.</summary>
    public bool IsOtherProgressUnknown => OtherProgress <= 0;

    [ObservableProperty] private string _otherStatus = string.Empty;

    /// <summary>"Their history: 12,345 plays from 2019-03-02 to 2024-06-01, read from my_spotify_data.zip."</summary>
    [ObservableProperty] private string _otherSummary = string.Empty;

    [ObservableProperty] private string _overlapText = "-";
    [ObservableProperty] private string _inCommonText = string.Empty;
    [ObservableProperty] private IReadOnlyList<ComparisonRow> _sharedArtists = Array.Empty<ComparisonRow>();

    /// <summary>Set by the window as the Compare tab comes and goes.</summary>
    [ObservableProperty] private bool _isVisible;

    partial void OnIsPeopleModeChanged(bool value)
    {
        // A comparison still running for the other mode would land in this one's tables.
        _cts?.Cancel();
        Message = string.Empty;
        ClearResults();
        Invalidate();
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (value && _stale)
            _ = RefreshAsync();
    }

    partial void OnStartAChanged(DateTime? value) => PeriodsChanged();
    partial void OnEndAChanged(DateTime? value) => PeriodsChanged();
    partial void OnStartBChanged(DateTime? value) => PeriodsChanged();
    partial void OnEndBChanged(DateTime? value) => PeriodsChanged();

    private void PeriodsChanged()
    {
        foreach (var option in RangesA)
            option.IsActive = option.Matches(StartA, EndA);
        foreach (var option in RangesB)
            option.IsActive = option.Matches(StartB, EndB);
        PeriodALabel = Describe("Period A", StartA, EndA);
        PeriodBLabel = Describe("Period B", StartB, EndB);
        if (!_settingPeriods)
            Invalidate();
    }

    private static string Describe(string name, DateTime? start, DateTime? end) => (start, end) switch
    {
        ({ } s, { } e) => $"{name}: {TimeFormat.Day(s)} to {TimeFormat.Day(e)}",
        ({ } s, null) => $"{name}: from {TimeFormat.Day(s)}",
        (null, { } e) => $"{name}: until {TimeFormat.Day(e)}",
        _ => $"{name}: all time",
    };

    /// <summary>
    /// Offers quick ranges for the loaded history and, the first time, starts with the last 12
    /// months against the 12 before them: two full stretches of the same length, where "this
    /// year against last" would put a few months against a whole year.
    /// </summary>
    public void SetHistory(DateTime? lastListen, IReadOnlyList<int> years, bool resetPeriods)
    {
        RangesA.Clear();
        RangesB.Clear();
        foreach (var option in DateRangeOption.For(lastListen, years))
            RangesA.Add(option);
        foreach (var option in DateRangeOption.For(lastListen, years))
            RangesB.Add(option);

        if (resetPeriods && lastListen is { } last)
        {
            var endB = last.Date;
            var startB = endB.AddMonths(-DateRangeOption.RecentMonths).AddDays(1);
            SetPeriods(startB.AddMonths(-DateRangeOption.RecentMonths), startB.AddDays(-1), startB, endB);
        }
        else
        {
            PeriodsChanged();
        }
    }

    /// <summary>The sidebar changed, so the comparison is out of date.</summary>
    public void Invalidate()
    {
        _stale = true;
        if (IsVisible)
            _ = RefreshAsync();
    }

    private void SetPeriods(DateTime? startA, DateTime? endA, DateTime? startB, DateTime? endB)
    {
        _settingPeriods = true;
        try
        {
            StartA = startA;
            EndA = endA;
            StartB = startB;
            EndB = endB;
        }
        finally
        {
            _settingPeriods = false;
        }
        PeriodsChanged();
    }

    [RelayCommand]
    private void ApplyRangeA(DateRangeOption? option)
    {
        if (option is not null)
            SetPeriods(option.Start, option.End, StartB, EndB);
    }

    [RelayCommand]
    private void ApplyRangeB(DateRangeOption? option)
    {
        if (option is not null)
            SetPeriods(StartA, EndA, option.Start, option.End);
    }

    [RelayCommand]
    private void Swap() => SetPeriods(StartB, EndB, StartA, EndA);

    /// <summary>
    /// Makes period A the stretch of the same length that ends the day before B starts. When
    /// B is made of whole calendar months (a month, a year), A is the same number of whole
    /// months, so 2025 is matched with 2024 even though one of them is a day longer.
    /// </summary>
    [RelayCommand]
    private void MatchAToB()
    {
        if (StartB is not { } start || EndB is not { } end || end < start)
        {
            Message = "Give period B a start and an end first, then A can be set to the stretch just before it.";
            return;
        }

        start = start.Date;
        end = end.Date;
        bool wholeMonths = start.Day == 1 && end.Day == DateTime.DaysInMonth(end.Year, end.Month);
        int months = (end.Year - start.Year) * 12 + end.Month - start.Month + 1;
        int days = (end - start).Days + 1;

        // A B that runs to 9999 to mean "no end" leaves no room for as long a stretch before it.
        bool fits = wholeMonths
            ? (start.Year - 1) * 12 + start.Month - 1 >= months
            : (start - DateTime.MinValue).Days >= days;
        if (!fits)
        {
            Message = "There's no room before period B for a stretch as long as it. Set period A by hand instead.";
            return;
        }

        var startA = wholeMonths ? start.AddMonths(-months) : start.AddDays(-days);
        SetPeriods(startA, start.AddDays(-1), StartB, EndB);
    }

    public async Task RefreshAsync()
    {
        if (_source() is not { } source)
            return;

        var other = _other;
        if (IsPeopleMode && other is null)
        {
            ClearResults();
            _stale = false;
            return;
        }

        if (!IsPeopleMode && (StartA > EndA || StartB > EndB))
        {
            Message = "A period starts after it ends. Swap its dates, or clear one of them.";
            ClearResults();
            _stale = false;
            return;
        }

        var previous = _cts;
        _cts = new CancellationTokenSource();
        previous?.Cancel();
        var token = _cts.Token;

        IsWorking = true;
        try
        {
            bool people = other is not null && IsPeopleMode;
            var result = people
                ? await PeriodComparison.ComparePeopleAsync(source.Records, other!, source.Filter, source.SessionGap, token)
                : await PeriodComparison.CompareAsync(source.Records, source.Filter,
                    StartA, EndA, StartB, EndB, source.SessionGap, token);
            if (token.IsCancellationRequested)
                return;

            Metrics = result.Metrics;
            Artists = result.Artists;
            Tracks = result.Tracks;
            NewInB = result.NewInB;
            GoneSinceA = result.GoneSinceA;
            SharedArtists = result.SharedArtists;
            OverlapText = Percent(result.OverlapPercent);
            InCommonText = result.ArtistsInCommon == 0
                ? "No artists in common"
                : $"{Plural.Of(result.ArtistsInCommon, "artist")} in common";
            Message = (people, result.A.TotalPlays, result.B.TotalPlays) switch
            {
                (true, 0, 0) => "Neither history has any plays under the current filters.",
                (true, 0, _) => "Their history has no plays under the current filters. Dates set in the sidebar may not cover any of their listening.",
                (true, _, 0) => "Your history has no plays under the current filters.",
                (false, 0, 0) => "Neither period has any plays under the current filters.",
                (false, 0, _) => "Period A has no plays under the current filters, so everything in B shows as new.",
                (false, _, 0) => "Period B has no plays under the current filters, so everything in A shows as gone.",
                _ => string.Empty,
            };
            _stale = false;
        }
        catch (OperationCanceledException)
        {
            // A newer refresh took over.
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsWorking = false;
            previous?.Dispose();
        }
    }

    private void ClearResults()
    {
        Metrics = Array.Empty<ComparisonMetric>();
        Artists = Array.Empty<ComparisonRow>();
        Tracks = Array.Empty<ComparisonRow>();
        NewInB = Array.Empty<ComparisonRow>();
        GoneSinceA = Array.Empty<ComparisonRow>();
        SharedArtists = Array.Empty<ComparisonRow>();
        OverlapText = "-";
        InCommonText = string.Empty;
    }

    private static string Percent(double value) => value switch
    {
        0 => "0%",
        < 1 => "<1%",
        _ => $"{value:0}%",
    };

    // ---- Someone else's history --------------------------------------------------------------

    [RelayCommand]
    private async Task OpenOtherFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select their Spotify history: the export ZIP or its JSON files",
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
        await LoadOtherAsync(FindFiles, picked.Length == 1 ? Path.GetFileName(picked[0]) : $"{picked.Length} files");
    }

    [RelayCommand]
    private async Task OpenOtherFolderAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Select the folder that holds their Spotify streaming history" };
        if (dialog.ShowDialog() != true)
            return;

        string folder = dialog.FolderName;
        string name = Path.GetFileName(folder.TrimEnd('\\', '/'));
        await LoadOtherAsync(() => HistoryParser.FindHistoryFiles(folder), name.Length > 0 ? name : folder);
    }

    /// <summary>
    /// Reads someone else's export for the comparison, from the files <paramref name="findFiles"/>
    /// turns up. <paramref name="name"/> says where it came from. It stays in memory only: the
    /// record cache holds a single history, and theirs would push out the one loaded.
    /// </summary>
    public async Task LoadOtherAsync(Func<IReadOnlyList<string>> findFiles, string name)
    {
        if (IsLoadingOther)
            return;

        _otherCts?.Dispose();
        _otherCts = new CancellationTokenSource();
        var token = _otherCts.Token;

        IsLoadingOther = true;
        Message = string.Empty;
        OtherProgress = 0;
        OtherStatus = "Looking for their Spotify history...";
        try
        {
            var files = await Task.Run(findFiles, token);
            if (files.Count == 0)
            {
                Message = "No Spotify history was found there. Pick the ZIP Spotify sent them, the folder it unpacks to, or its JSON files.";
                return;
            }
            if (_source()?.Files is { Count: > 0 } yours && yours.SequenceEqual(files, StringComparer.OrdinalIgnoreCase))
            {
                Message = "That's the export already open as yours. Pick the other person's.";
                return;
            }

            var progress = new Progress<ParseProgress>(p =>
            {
                OtherProgress = p.Percent;
                OtherStatus = p.Message;
            });
            var timeZones = _source()?.TimeZones;
            var parsed = await new HistoryParser(timeZones).ParseAsync(files, progress, token);
            token.ThrowIfCancellationRequested();
            if (parsed.Records.Count == 0)
            {
                Message = parsed.Warnings.Count > 0
                    ? $"No listening data was found in their files. {parsed.Warnings[0]}"
                    : "No listening data was found in their files.";
                return;
            }

            _other = parsed.Records;
            _otherFiles = findFiles;
            _otherName = name;
            _otherTimeZones = (timeZones ?? PlayTimeZones.ThisPc).CacheKey;
            OtherSummary = Summarise(parsed.Records, name);
            HasOther = true;
            Invalidate();
        }
        catch (OperationCanceledException)
        {
            // Cancelled from the button; whatever was open before stays open.
        }
        finally
        {
            IsLoadingOther = false;
            OtherProgress = 0;
        }
    }

    [RelayCommand(CanExecute = nameof(IsLoadingOther))]
    private void CancelOther() => _otherCts?.Cancel();

    /// <summary>Reads their export again if play times are now converted differently from when it was read.</summary>
    public Task ReadOtherAgainIfTimesChangedAsync()
    {
        string now = (_source()?.TimeZones ?? PlayTimeZones.ThisPc).CacheKey;
        return HasOther && _otherFiles is { } files && now != _otherTimeZones
            ? LoadOtherAsync(files, _otherName)
            : Task.CompletedTask;
    }

    /// <summary>Forgets the other history, which frees its memory.</summary>
    [RelayCommand]
    private void CloseOther()
    {
        _otherCts?.Cancel();
        _cts?.Cancel();
        _other = null;
        _otherFiles = null;
        HasOther = false;
        OtherSummary = string.Empty;
        Message = string.Empty;
        ClearResults();
    }

    private static string Summarise(List<PlayRecord> records, string name)
    {
        var (first, last) = PlayRecord.DateSpan(records);
        string plays = Plural.Of(records.Count, "play");
        return first is { } f && last is { } l
            ? $"Their history: {plays} from {TimeFormat.Day(f)} to {TimeFormat.Day(l)}, read from {name}."
            : $"Their history: {plays}, read from {name}.";
    }
}
