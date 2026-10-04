using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sortify.Models;
using Sortify.Services;

namespace Sortify.ViewModels;

/// <summary>
/// The Compare tab: two date ranges of the loaded history side by side, under the sidebar's
/// other filters. Only works anything out while the tab is on screen; changes made while it
/// is hidden are picked up when it is shown again.
/// </summary>
public sealed partial class CompareViewModel : ObservableObject
{
    /// <summary>What a comparison runs over: the plays, the sidebar filters and the session gap.</summary>
    public readonly record struct Source(IReadOnlyList<PlayRecord> Records, FilterOptions Filter, TimeSpan SessionGap);

    private readonly Func<Source?> _source;
    private CancellationTokenSource? _cts;
    private bool _stale = true;
    private bool _settingPeriods;

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

    /// <summary>Set by the window as the Compare tab comes and goes.</summary>
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
        var endA = start.AddDays(-1);
        bool wholeMonths = start.Day == 1 && end.AddDays(1).Day == 1;
        if (wholeMonths)
        {
            int months = (end.Year - start.Year) * 12 + end.Month - start.Month + 1;
            SetPeriods(start.AddMonths(-months), endA, StartB, EndB);
        }
        else
        {
            SetPeriods(endA.AddDays(-(end - start).Days), endA, StartB, EndB);
        }
    }

    public async Task RefreshAsync()
    {
        if (_source() is not { } source)
            return;

        if (StartA > EndA || StartB > EndB)
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
            var result = await PeriodComparison.CompareAsync(source.Records, source.Filter,
                StartA, EndA, StartB, EndB, source.SessionGap, token);
            if (token.IsCancellationRequested)
                return;

            Metrics = result.Metrics;
            Artists = result.Artists;
            Tracks = result.Tracks;
            NewInB = result.NewInB;
            GoneSinceA = result.GoneSinceA;
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
    }
}
