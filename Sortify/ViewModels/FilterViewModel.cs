using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sortify.Models;

namespace Sortify.ViewModels;

/// <summary>
/// Bindable wrapper around <see cref="FilterOptions"/>. Raises <see cref="FiltersChanged"/>
/// whenever any filter value changes so the owner can re-run analysis.
/// </summary>
public sealed partial class FilterViewModel : ObservableObject
{
    public event EventHandler? FiltersChanged;

    private bool _suppress;

    [ObservableProperty] private int _minSeconds = FilterOptions.DefaultMinMs / 1000;
    [ObservableProperty] private DateTime? _startDate;
    [ObservableProperty] private DateTime? _endDate;
    [ObservableProperty] private string _searchTerm = string.Empty;
    [ObservableProperty] private int _startHour;
    [ObservableProperty] private int _endHour = 23;

    [ObservableProperty] private string _newExcludedArtist = string.Empty;
    [ObservableProperty] private string _newExcludedTrack = string.Empty;

    public ObservableCollection<string> ExcludedArtists { get; } = new();
    public ObservableCollection<string> ExcludedTracks { get; } = new();

    public DayToggle[] Days { get; }

    public FilterViewModel()
    {
        Days = new[]
        {
            new DayToggle("Sun", 0, this),
            new DayToggle("Mon", 1, this),
            new DayToggle("Tue", 2, this),
            new DayToggle("Wed", 3, this),
            new DayToggle("Thu", 4, this),
            new DayToggle("Fri", 5, this),
            new DayToggle("Sat", 6, this),
        };
        ExcludedArtists.CollectionChanged += (_, _) => Raise();
        ExcludedTracks.CollectionChanged += (_, _) => Raise();
    }

    partial void OnMinSecondsChanged(int value) => Raise();
    partial void OnStartDateChanged(DateTime? value) => Raise();
    partial void OnEndDateChanged(DateTime? value) => Raise();
    partial void OnSearchTermChanged(string value) => Raise();
    partial void OnStartHourChanged(int value) => Raise();
    partial void OnEndHourChanged(int value) => Raise();

    internal void Raise()
    {
        if (_suppress) return;
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Builds a fresh <see cref="FilterOptions"/> snapshot from current values.</summary>
    public FilterOptions ToOptions()
    {
        var opts = new FilterOptions
        {
            MinMsPlayed = Math.Max(0, MinSeconds) * 1000,
            StartDate = StartDate?.Date,
            EndDate = EndDate?.Date.AddDays(1).AddTicks(-1),
            SearchTerm = SearchTerm ?? string.Empty,
            StartHour = Math.Clamp(StartHour, 0, 23),
            EndHour = Math.Clamp(EndHour, 0, 23),
        };
        foreach (var a in ExcludedArtists) opts.ExcludedArtists.Add(a);
        foreach (var t in ExcludedTracks) opts.ExcludedTracks.Add(t);
        foreach (var d in Days) opts.IncludedDaysOfWeek[d.Index] = d.IsSelected;
        return opts;
    }

    [RelayCommand]
    private void AddExcludedArtist()
    {
        var name = NewExcludedArtist?.Trim();
        if (!string.IsNullOrEmpty(name) && !ExcludedArtists.Contains(name))
            ExcludedArtists.Add(name);
        NewExcludedArtist = string.Empty;
    }

    [RelayCommand]
    private void RemoveExcludedArtist(string? name)
    {
        if (name is not null) ExcludedArtists.Remove(name);
    }

    [RelayCommand]
    private void AddExcludedTrack()
    {
        var name = NewExcludedTrack?.Trim();
        if (!string.IsNullOrEmpty(name) && !ExcludedTracks.Contains(name))
            ExcludedTracks.Add(name);
        NewExcludedTrack = string.Empty;
    }

    [RelayCommand]
    private void RemoveExcludedTrack(string? name)
    {
        if (name is not null) ExcludedTracks.Remove(name);
    }

    [RelayCommand]
    private void Reset()
    {
        _suppress = true;
        MinSeconds = FilterOptions.DefaultMinMs / 1000;
        StartDate = null;
        EndDate = null;
        SearchTerm = string.Empty;
        StartHour = 0;
        EndHour = 23;
        NewExcludedArtist = string.Empty;
        NewExcludedTrack = string.Empty;
        ExcludedArtists.Clear();
        ExcludedTracks.Clear();
        foreach (var d in Days) d.IsSelected = true;
        _suppress = false;
        Raise();
    }
}

/// <summary>A single day-of-week checkbox state.</summary>
public sealed partial class DayToggle : ObservableObject
{
    private readonly FilterViewModel _owner;

    public string Label { get; }
    public int Index { get; }

    [ObservableProperty] private bool _isSelected = true;

    public DayToggle(string label, int index, FilterViewModel owner)
    {
        Label = label;
        Index = index;
        _owner = owner;
    }

    partial void OnIsSelectedChanged(bool value) => _owner.Raise();
}
