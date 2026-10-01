using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sortify.Models;
using Sortify.Services;

namespace Sortify.ViewModels;

/// <summary>
/// Bindable wrapper around <see cref="FilterOptions"/>. Raises <see cref="FiltersChanged"/>
/// whenever any filter value changes so the owner can re-run analysis.
/// </summary>
public sealed partial class FilterViewModel : ObservableObject
{
    public event EventHandler? FiltersChanged;

    /// <summary>Raised when a preset is saved or deleted, so the owner can persist the list.</summary>
    public event EventHandler? PresetsChanged;

    /// <summary>A line for the status bar about something the presets did.</summary>
    public event EventHandler<string>? Notice;

    private bool _suppress;

    /// <summary>True while a preset is being applied, so the change doesn't deselect it again.</summary>
    private bool _applyingPreset;

    /// <summary>True while the preset list's selection is moved without applying anything.</summary>
    private bool _selectingQuietly;

    [ObservableProperty] private int _minSeconds = FilterOptions.DefaultMinMs / 1000;
    [ObservableProperty] private bool _includePodcasts;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInvalidDateRange))]
    private DateTime? _startDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInvalidDateRange))]
    private DateTime? _endDate;
    [ObservableProperty] private string _searchTerm = string.Empty;
    [ObservableProperty] private int _startHour;
    [ObservableProperty] private int _endHour = 23;

    [ObservableProperty] private string _newExcludedArtist = string.Empty;
    [ObservableProperty] private string _newExcludedTrack = string.Empty;

    public ObservableCollection<string> ExcludedArtists { get; } = new();
    public ObservableCollection<string> ExcludedTracks { get; } = new();

    public DayToggle[] Days { get; }

    /// <summary>Saved filter sets, in the order they were made.</summary>
    public ObservableCollection<FilterPreset> Presets { get; } = new();

    /// <summary>
    /// The preset whose filters are in effect. Picking one applies it; changing any filter
    /// afterwards clears it, since the sidebar no longer matches what was saved.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeletePresetCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelectedPreset))]
    private FilterPreset? _selectedPreset;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SavePresetCommand))]
    private string _newPresetName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresetPrompt))]
    private bool _hasPresets;

    public bool HasSelectedPreset => SelectedPreset is not null;

    /// <summary>Shown in the preset list while nothing in it is picked.</summary>
    public string PresetPrompt => HasPresets ? "Choose a saved set" : "Nothing saved yet";

    /// <summary>
    /// True when the From date is after the To date. The filter still applies as asked (and
    /// matches nothing); this only exists so the panel can say why everything vanished.
    /// </summary>
    public bool HasInvalidDateRange => StartDate is { } start && EndDate is { } end && start.Date > end.Date;

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
        Presets.CollectionChanged += (_, _) => HasPresets = Presets.Count > 0;
    }

    partial void OnMinSecondsChanged(int value) => Raise();
    partial void OnIncludePodcastsChanged(bool value) => Raise();
    partial void OnStartDateChanged(DateTime? value) => Raise();
    partial void OnEndDateChanged(DateTime? value) => Raise();
    partial void OnSearchTermChanged(string value) => Raise();
    partial void OnStartHourChanged(int value) => Raise();
    partial void OnEndHourChanged(int value) => Raise();

    internal void Raise()
    {
        if (_suppress) return;
        if (!_applyingPreset)
            SelectQuietly(null);
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Presets -----------------------------------------------------------------------------

    /// <summary>Every filter as it stands, under <paramref name="name"/>.</summary>
    public FilterPreset Snapshot(string name = "") => new()
    {
        Name = name,
        MinSeconds = MinSeconds,
        IncludePodcasts = IncludePodcasts,
        StartDate = StartDate?.Date,
        EndDate = EndDate?.Date,
        SearchTerm = SearchTerm ?? string.Empty,
        StartHour = StartHour,
        EndHour = EndHour,
        IncludedDays = Days.Select(d => d.IsSelected).ToArray(),
        ExcludedArtists = ExcludedArtists.ToList(),
        ExcludedTracks = ExcludedTracks.ToList(),
    };

    /// <summary>
    /// Just the filters that are remembered between launches: what the user never wants
    /// counted. Everything else is left at its default.
    /// </summary>
    public FilterPreset RememberedSnapshot() => new()
    {
        MinSeconds = MinSeconds,
        IncludePodcasts = IncludePodcasts,
        ExcludedArtists = ExcludedArtists.ToList(),
        ExcludedTracks = ExcludedTracks.ToList(),
    };

    /// <summary>Replaces every filter with the preset's, raising a single change.</summary>
    public void Apply(FilterPreset preset)
    {
        _suppress = true;
        try
        {
            MinSeconds = Math.Max(0, preset.MinSeconds);
            IncludePodcasts = preset.IncludePodcasts;
            StartDate = preset.StartDate;
            EndDate = preset.EndDate;
            SearchTerm = preset.SearchTerm ?? string.Empty;
            StartHour = Math.Clamp(preset.StartHour, 0, 23);
            EndHour = Math.Clamp(preset.EndHour, 0, 23);

            ExcludedArtists.Clear();
            foreach (var artist in preset.ExcludedArtists ?? new List<string>())
                ExcludeArtist(artist);
            ExcludedTracks.Clear();
            foreach (var track in preset.ExcludedTracks ?? new List<string>())
                ExcludeTrack(track);

            for (int i = 0; i < Days.Length; i++)
                Days[i].IsSelected = preset.IncludedDays is { Length: 7 } days ? days[i] : true;
        }
        finally
        {
            _suppress = false;
        }

        _applyingPreset = true;
        try { Raise(); }
        finally { _applyingPreset = false; }
    }

    /// <summary>Fills the preset list from settings, without applying any of them.</summary>
    public void LoadPresets(IEnumerable<FilterPreset> presets)
    {
        Presets.Clear();
        foreach (var preset in presets)
            Presets.Add(preset);
    }

    partial void OnSelectedPresetChanged(FilterPreset? value)
    {
        if (value is not null && !_selectingQuietly)
            Apply(value);
    }

    private void SelectQuietly(FilterPreset? preset)
    {
        _selectingQuietly = true;
        try { SelectedPreset = preset; }
        finally { _selectingQuietly = false; }
    }

    private bool CanSavePreset() => !string.IsNullOrWhiteSpace(NewPresetName);

    /// <summary>Saves the current filters under the typed name, replacing a preset of that name.</summary>
    [RelayCommand(CanExecute = nameof(CanSavePreset))]
    private void SavePreset()
    {
        string name = NewPresetName.Trim();
        var preset = Snapshot(name);

        int existing = Presets.ToList().FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            Presets[existing] = preset;
        }
        else if (Presets.Count >= AppSettings.MaxFilterPresets)
        {
            Notice?.Invoke(this, $"You already have {AppSettings.MaxFilterPresets} saved filter sets. " +
                                 "Delete one before saving another.");
            return;
        }
        else
        {
            Presets.Add(preset);
        }

        NewPresetName = string.Empty;
        SelectQuietly(preset);
        PresetsChanged?.Invoke(this, EventArgs.Empty);
        Notice?.Invoke(this, existing >= 0
            ? $"Updated the saved filters “{name}”."
            : $"Saved the current filters as “{name}”.");
    }

    [RelayCommand(CanExecute = nameof(CanDeletePreset))]
    private void DeletePreset()
    {
        if (SelectedPreset is not { } preset)
            return;

        SelectQuietly(null);
        Presets.Remove(preset);
        PresetsChanged?.Invoke(this, EventArgs.Empty);
        Notice?.Invoke(this, $"Deleted the saved filters “{preset.Name}”. The filters themselves are unchanged.");
    }

    private bool CanDeletePreset() => SelectedPreset is not null;

    /// <summary>Builds a fresh <see cref="FilterOptions"/> snapshot from current values.</summary>
    public FilterOptions ToOptions()
    {
        var opts = new FilterOptions
        {
            // long math + clamp so an absurd seconds value can't overflow to a negative cutoff.
            MinMsPlayed = (int)Math.Clamp(MinSeconds * 1000L, 0L, int.MaxValue),
            IncludePodcasts = IncludePodcasts,
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

    /// <summary>
    /// One short phrase per filter that is currently narrowing the results, for the chip
    /// row above the tabs. Empty when nothing but the defaults are in effect.
    /// </summary>
    public IEnumerable<string> Describe()
    {
        if (MinSeconds != FilterOptions.DefaultMinMs / 1000)
            yield return $"Min {MinSeconds}s";

        if (IncludePodcasts)
            yield return "Podcasts counted";

        if (StartDate is { } start && EndDate is { } end)
            yield return $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}";
        else if (StartDate is { } from)
            yield return $"From {from:yyyy-MM-dd}";
        else if (EndDate is { } to)
            yield return $"Until {to:yyyy-MM-dd}";

        if (!string.IsNullOrWhiteSpace(SearchTerm))
            yield return $"Search “{SearchTerm.Trim()}”";

        if (StartHour != 0 || EndHour != 23)
            yield return $"{StartHour:00}:00-{EndHour:00}:59";

        var days = Days.Where(d => d.IsSelected).Select(d => d.Label).ToList();
        if (days.Count < Days.Length)
            yield return days.Count == 0 ? "No days selected" : string.Join(", ", days);

        if (ExcludedArtists.Count > 0)
            yield return $"{ExcludedArtists.Count} artist{(ExcludedArtists.Count == 1 ? "" : "s")} excluded";

        if (ExcludedTracks.Count > 0)
            yield return $"{ExcludedTracks.Count} track{(ExcludedTracks.Count == 1 ? "" : "s")} excluded";
    }

    /// <summary>Adds an artist exclusion programmatically (e.g. from a grid context menu).</summary>
    public void ExcludeArtist(string? name)
    {
        name = name?.Trim();
        if (!string.IsNullOrEmpty(name) && !ExcludedArtists.Contains(name, StringComparer.OrdinalIgnoreCase))
            ExcludedArtists.Add(name);
    }

    /// <summary>Adds a track exclusion programmatically (e.g. from a grid context menu).</summary>
    public void ExcludeTrack(string? name)
    {
        name = name?.Trim();
        if (!string.IsNullOrEmpty(name) && !ExcludedTracks.Contains(name, StringComparer.OrdinalIgnoreCase))
            ExcludedTracks.Add(name);
    }

    [RelayCommand]
    private void AddExcludedArtist()
    {
        ExcludeArtist(NewExcludedArtist);
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
        ExcludeTrack(NewExcludedTrack);
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
        IncludePodcasts = false;
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
