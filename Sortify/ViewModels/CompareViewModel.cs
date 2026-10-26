using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sortify.Models;
using Sortify.Services;

namespace Sortify.ViewModels;

/// <summary>
/// The Compare Periods tab: two date ranges of the loaded history side by side, under the sidebar's
/// other filters. Only works anything out while the tab is on screen; changes made while it
/// is hidden are picked up when it is shown again.
/// </summary>
public sealed partial class CompareViewModel : ObservableObject
{
    /// <summary>
    /// What a comparison runs over: the plays, the sidebar filters, the session gap, the
    /// files the plays were read from (so the same export can't be opened as someone else's
    /// on the Compare People tab) and how play times are converted, which someone else's
    /// export is read with too.
    /// </summary>
    public readonly record struct Source(
        IReadOnlyList<PlayRecord> Records, FilterOptions Filter, TimeSpan SessionGap,
        IReadOnlyList<string>? Files = null, PlayTimeZones? TimeZones = null);

    private readonly Func<Source?> _source;
    private CancellationTokenSource? _cts;
    private bool _stale = true;
    private bool _settingPeriods;
    private IReadOnlyList<int> _years = Array.Empty<int>();

    public CompareViewModel(Func<Source?> source) => _source = source;

    [ObservableProperty] private DateTime? _startA;
    [ObservableProperty] private DateTime? _endA;
    [ObservableProperty] private DateTime? _startB;
    [ObservableProperty] private DateTime? _endB;

    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string _periodALabel = "Period A";
    [ObservableProperty] private string _periodBLabel = "Period B";

    [ObservableProperty] private IReadOnlyList<ComparisonMetric> _metrics = Array.Empty<ComparisonMetric>();
    [ObservableProperty] private IReadOnlyList<ComparisonRow> _artists = Array.Empty<ComparisonRow>();
    [ObservableProperty] private IReadOnlyList<ComparisonRow> _tracks = Array.Empty<ComparisonRow>();
    [ObservableProperty] private IReadOnlyList<ComparisonRow> _newInB = Array.Empty<ComparisonRow>();
    [ObservableProperty] private IReadOnlyList<ComparisonRow> _goneSinceA = Array.Empty<ComparisonRow>();

    /// <summary>
    /// What the tables rank by; follows the main window's Time / Plays switch. Changing it
    /// re-ranks the last comparison rather than analysing both periods again.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RankingHint), nameof(NewInBHint), nameof(GoneSinceAHint),
        nameof(NewInBSortMember), nameof(GoneSinceASortMember))]
    private Metric _metric;

    /// <summary>The two periods last analysed, kept so a change of metric only has to re-rank them.</summary>
    private PeriodComparisonResult? _last;

    public string RankingHint => Metric == Metric.Plays
        ? "Ranked by plays in period B. ▲ climbed since A, ▼ fell, new wasn't played in A, out isn't played in B."
        : "Ranked by listening time in period B. ▲ climbed since A, ▼ fell, new wasn't played in A, out isn't played in B.";

    public string NewInBHint => Metric == Metric.Plays
        ? "Artists you listened to for at least an hour in B and not at all in A, most played first."
        : "Artists you listened to for at least an hour in B and not at all in A.";

    public string GoneSinceAHint => Metric == Metric.Plays
        ? "Artists you listened to for at least an hour in A and not at all in B, most played first."
        : "Artists you listened to for at least an hour in A and not at all in B.";

    // The column the new and gone lists arrive sorted by, so their headers can show the arrow.
    public string NewInBSortMember => Metric == Metric.Plays ? nameof(ComparisonRow.PlaysB) : nameof(ComparisonRow.MsB);
    public string GoneSinceASortMember => Metric == Metric.Plays ? nameof(ComparisonRow.PlaysA) : nameof(ComparisonRow.MsA);

    partial void OnMetricChanged(Metric value)
    {
        if (_last is { } last)
            Show(PeriodComparison.Build(last.A, last.B, value));
    }

    /// <summary>Set by the window as the Compare Periods tab comes and goes.</summary>
    [ObservableProperty] private bool _isVisible;

    partial void OnIsVisibleChanged(bool value)
    {
        if (value && _stale)
            _ = RefreshAsync();
    }

    partial void OnStartAChanged(DateTime? value) => PeriodsChanged();
    partial void OnEndAChanged(DateTime? value) => PeriodsChanged();
    partial void OnStartBChanged(DateTime? value) => PeriodsChanged();
    partial void OnEndBChanged(DateTime? value) => PeriodsChanged();

    /// <summary>Period B's days in each other year, offered one click away under period A.</summary>
    public ObservableCollection<DateRangeOption> SameDaysA { get; } = new();

    /// <summary>Period A's days in each other year, offered one click away under period B.</summary>
    public ObservableCollection<DateRangeOption> SameDaysB { get; } = new();

    [ObservableProperty] private bool _hasSameDaysA;
    [ObservableProperty] private bool _hasSameDaysB;

    /// <summary>The year picked in period A's dropdown, or null while A is not one of them.</summary>
    [ObservableProperty] private DateRangeOption? _selectedSameDaysA;

    /// <summary>The year picked in period B's dropdown, or null while B is not one of them.</summary>
    [ObservableProperty] private DateRangeOption? _selectedSameDaysB;

    partial void OnSelectedSameDaysAChanged(DateRangeOption? value)
    {
        if (value is not null && !value.IsActive)
            ApplyRangeA(value);
    }

    partial void OnSelectedSameDaysBChanged(DateRangeOption? value)
    {
        if (value is not null && !value.IsActive)
            ApplyRangeB(value);
    }

    private static void Fill(ObservableCollection<DateRangeOption> target, IReadOnlyList<DateRangeOption> options, DateTime? start, DateTime? end)
    {
        if (!target.Select(o => (o.Start, o.End)).SequenceEqual(options.Select(o => (o.Start, o.End))))
        {
            target.Clear();
            foreach (var option in options)
                target.Add(option);
        }
        foreach (var option in target)
            option.IsActive = option.Matches(start, end);
    }

    private void PeriodsChanged()
    {
        Fill(SameDaysA, SameDaysInOtherYears(forA: true), StartA, EndA);
        Fill(SameDaysB, SameDaysInOtherYears(forA: false), StartB, EndB);
        HasSameDaysA = SameDaysA.Count > 0;
        HasSameDaysB = SameDaysB.Count > 0;
        SelectedSameDaysA = SameDaysA.FirstOrDefault(o => o.IsActive);
        SelectedSameDaysB = SameDaysB.FirstOrDefault(o => o.IsActive);
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
    /// Notes the years the loaded history covers, for <see cref="SameDaysInOtherYears"/>, and,
    /// the first time, starts with the last 12 months against the 12 before them: two full
    /// stretches of the same length, where "this year against last" would put a few months
    /// against a whole year.
    /// </summary>
    public void SetHistory(DateTime? lastListen, IReadOnlyList<int> years, bool resetPeriods)
    {
        _years = years.Distinct().OrderByDescending(y => y).ToList();

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

    /// <summary>Ends period A today.</summary>
    [RelayCommand]
    private void EndANow() => SetPeriods(StartA, DateTime.Today, StartB, EndB);

    /// <summary>Ends period B today.</summary>
    [RelayCommand]
    private void EndBNow() => SetPeriods(StartA, EndA, StartB, DateTime.Today);

    /// <summary>
    /// The other period's days moved into each other year of the history, keeping their month
    /// and day, newest year first: what period A (or B) can be set to so it covers the same
    /// stretch of another year. Each is labelled by the year it ends in. A period that runs to
    /// the end of a month keeps doing so, so February takes in the 29th in a leap year. Empty
    /// until the other period has both a start and an end.
    /// </summary>
    public IReadOnlyList<DateRangeOption> SameDaysInOtherYears(bool forA)
    {
        var (start, end) = forA ? (StartB, EndB) : (StartA, EndA);
        if (start is not { } s || end is not { } e || e < s)
            return Array.Empty<DateRangeOption>();

        s = s.Date;
        e = e.Date;
        // Not e.AddDays(1): an end of 31 December 9999 has no next day.
        bool toMonthEnd = e.Day == DateTime.DaysInMonth(e.Year, e.Month);
        var options = new List<DateRangeOption>();
        foreach (int year in _years)
        {
            // A period that runs to 9999 to mean "no end" can't move back so far that it
            // would have to start before year 1.
            if (year == e.Year || s.Year + year - e.Year < 1)
                continue;
            var newStart = s.AddYears(year - e.Year);
            var newEnd = e.AddYears(year - e.Year);
            if (toMonthEnd)
                newEnd = new DateTime(newEnd.Year, newEnd.Month, DateTime.DaysInMonth(newEnd.Year, newEnd.Month));
            options.Add(new(year.ToString(), newStart, newEnd, $"{TimeFormat.Day(newStart)} to {TimeFormat.Day(newEnd)}"));
        }
        return options;
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

        // Cancelled before the check below, so a comparison of the old periods can't finish
        // after them and fill the tables it just cleared.
        var previous = _cts;
        var cts = new CancellationTokenSource();
        _cts = cts;
        previous?.Cancel();
        var token = cts.Token;

        if (StartA > EndA || StartB > EndB)
        {
            Message = "A period starts after it ends. Swap its dates, or clear one of them.";
            ClearResults();
            IsWorking = false;
            _stale = false;
            return;
        }

        IsWorking = true;
        try
        {
            var metric = Metric;
            var result = await PeriodComparison.CompareAsync(source.Records, source.Filter,
                StartA, EndA, StartB, EndB, metric, source.SessionGap, token);
            if (token.IsCancellationRequested)
                return;

            // The switch may have been flipped while both periods were being worked out.
            Show(metric == Metric ? result : PeriodComparison.Build(result.A, result.B, Metric));
            Message = (result.A.TotalPlays, result.B.TotalPlays) switch
            {
                (0, 0) => "Neither period has any plays under the current filters.",
                (0, _) => "Period A has no plays under the current filters, so everything in B shows as new.",
                (_, 0) => "Period B has no plays under the current filters, so everything in A shows as gone.",
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
            // Whichever comparison ran last says it has stopped, even one cancelled without
            // another taking over.
            if (ReferenceEquals(_cts, cts))
                IsWorking = false;
            previous?.Dispose();
        }
    }

    private void Show(PeriodComparisonResult result)
    {
        _last = result;
        Metrics = result.Metrics;
        Artists = result.Artists;
        Tracks = result.Tracks;
        NewInB = result.NewInB;
        GoneSinceA = result.GoneSinceA;
    }

    private void ClearResults()
    {
        _last = null;
        Metrics = Array.Empty<ComparisonMetric>();
        Artists = Array.Empty<ComparisonRow>();
        Tracks = Array.Empty<ComparisonRow>();
        NewInB = Array.Empty<ComparisonRow>();
        GoneSinceA = Array.Empty<ComparisonRow>();
    }
}
