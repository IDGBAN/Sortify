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

    [ObservableProperty] private PlaybackMode _shuffleMode;
    [ObservableProperty] private PlaybackMode _offlineMode;
    [ObservableProperty] private PlaybackMode _privateMode;

    public IReadOnlyList<PlaybackChoice> ShuffleChoices { get; } = PlaybackChoice.For("shuffled plays", "Shuffled plays");
    public IReadOnlyList<PlaybackChoice> OfflineChoices { get; } = PlaybackChoice.For("offline plays", "Offline plays");
    public IReadOnlyList<PlaybackChoice> PrivateChoices { get; } = PlaybackChoice.For("private sessions", "Private sessions");

    /// <summary>One checkbox per device family in the loaded history.</summary>
    public ObservableCollection<ChoiceToggle> Devices { get; } = new();

    /// <summary>One checkbox per country in the loaded history, most listened first.</summary>
    public ObservableCollection<ChoiceToggle> Countries { get; } = new();

    // Kept apart from the checkboxes: a saved preset can leave out a device or country this
    // history never played on, and that should still hold when a history that did is loaded.
    private readonly HashSet<string> _excludedDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _excludedCountries = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private bool _hasPlaybackChoices;

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

    /// <summary>One-click ranges for the loaded history; empty until something is loaded.</summary>
    public ObservableCollection<DateRangeOption> QuickRanges { get; } = new();

    [ObservableProperty] private bool _hasQuickRanges;

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
    partial void OnStartDateChanged(DateTime? value)
    {
        RefreshQuickRanges();
        Raise();
    }

    partial void OnEndDateChanged(DateTime? value)
    {
        RefreshQuickRanges();
        Raise();
    }

    partial void OnSearchTermChanged(string value) => Raise();
    partial void OnStartHourChanged(int value) => Raise();
    partial void OnEndHourChanged(int value) => Raise();
    partial void OnShuffleModeChanged(PlaybackMode value) => Raise();
    partial void OnOfflineModeChanged(PlaybackMode value) => Raise();
    partial void OnPrivateModeChanged(PlaybackMode value) => Raise();

    // ---- Devices and countries ---------------------------------------------------------------

    /// <summary>
    /// Fills the device and country checkboxes from the loaded history. Anything left out
    /// before stays left out.
    /// </summary>
    public void SetAvailablePlayback(
        IEnumerable<(string Key, string Label, string Description)> devices,
        IEnumerable<(string Key, string Label, string Description)> countries)
    {
        Fill(Devices, devices, _excludedDevices);
        Fill(Countries, countries, _excludedCountries);
        HasPlaybackChoices = Devices.Count > 0 || Countries.Count > 0;

        void Fill(ObservableCollection<ChoiceToggle> target,
            IEnumerable<(string Key, string Label, string Description)> items, HashSet<string> excluded)
        {
            target.Clear();
            foreach (var (key, label, description) in items)
            {
                target.Add(new ChoiceToggle(key, label, description, !excluded.Contains(key),
                    t => OnChoiceToggled(t, excluded)));
            }
        }
    }

    private void OnChoiceToggled(ChoiceToggle toggle, HashSet<string> excluded)
    {
        bool changed = toggle.IsSelected ? excluded.Remove(toggle.Key) : excluded.Add(toggle.Key);
        if (changed)
            Raise();
    }

    /// <summary>Points every checkbox back at the excluded sets after they were replaced.</summary>
    private void SyncChoiceToggles()
    {
        foreach (var device in Devices)
            device.SetQuietly(!_excludedDevices.Contains(device.Key));
        foreach (var country in Countries)
            country.SetQuietly(!_excludedCountries.Contains(country.Key));
    }

    private void IncludeEvery(HashSet<string> excluded) => Batch(() =>
    {
        excluded.Clear();
        SyncChoiceToggles();
    });

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
        Shuffle = ShuffleMode,
        Offline = OfflineMode,
        Private = PrivateMode,
        ExcludedDevices = _excludedDevices.ToList(),
        ExcludedCountries = _excludedCountries.ToList(),
    };

    /// <summary>
    /// Just the filters that are remembered between launches: what the user never wants
    /// counted. Everything else is left at its default. Shuffle and offline are ways of
    /// looking at the history rather than plays to keep out, so they aren't remembered;
    /// private sessions usually are plays to keep out, so that choice is.
    /// </summary>
    public FilterPreset RememberedSnapshot() => new()
    {
        MinSeconds = MinSeconds,
        IncludePodcasts = IncludePodcasts,
        ExcludedArtists = ExcludedArtists.ToList(),
        ExcludedTracks = ExcludedTracks.ToList(),
        Private = PrivateMode,
        ExcludedDevices = _excludedDevices.ToList(),
        ExcludedCountries = _excludedCountries.ToList(),
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

            ShuffleMode = Enum.IsDefined(preset.Shuffle) ? preset.Shuffle : PlaybackMode.Any;
            OfflineMode = Enum.IsDefined(preset.Offline) ? preset.Offline : PlaybackMode.Any;
            PrivateMode = Enum.IsDefined(preset.Private) ? preset.Private : PlaybackMode.Any;
            _excludedDevices.Clear();
            _excludedDevices.UnionWith((preset.ExcludedDevices ?? new List<string>()).Where(d => !string.IsNullOrWhiteSpace(d)));
            _excludedCountries.Clear();
            _excludedCountries.UnionWith((preset.ExcludedCountries ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)));
            SyncChoiceToggles();
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
            Shuffle = ShuffleMode,
            Offline = OfflineMode,
            Private = PrivateMode,
        };
        foreach (var a in ExcludedArtists) opts.ExcludedArtists.Add(a);
        foreach (var t in ExcludedTracks) opts.ExcludedTracks.Add(t);
        foreach (var d in Days) opts.IncludedDaysOfWeek[d.Index] = d.IsSelected;
        opts.ExcludedDevices.UnionWith(_excludedDevices);
        opts.ExcludedCountries.UnionWith(_excludedCountries);
        return opts;
    }

    /// <summary>
    /// One short phrase per filter that is currently narrowing the results, for the chip
    /// row above the tabs. Empty when nothing but the defaults are in effect.
    /// </summary>
    public IEnumerable<string> Describe(bool includeDates = true) => Chips(includeDates).Select(c => c.Text);

    /// <summary>
    /// The filters currently narrowing the results, each able to clear itself without
    /// touching the others. Leaving out the dates suits views that set their own range.
    /// </summary>
    public IEnumerable<FilterChip> Chips(bool includeDates = true)
    {
        if (MinSeconds != FilterOptions.DefaultMinMs / 1000)
        {
            yield return new FilterChip($"Min {MinSeconds}s", "Go back to the default minimum duration",
                () => MinSeconds = FilterOptions.DefaultMinMs / 1000);
        }

        if (IncludePodcasts)
            yield return new FilterChip("Podcasts counted", "Stop counting podcasts", () => IncludePodcasts = false);

        string? range = (StartDate, EndDate) switch
        {
            ({ } start, { } end) => $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}",
            ({ } from, null) => $"From {from:yyyy-MM-dd}",
            (null, { } to) => $"Until {to:yyyy-MM-dd}",
            _ => null,
        };
        if (range is not null && includeDates)
        {
            yield return new FilterChip(range, "Clear the date range", () => Batch(() =>
            {
                StartDate = null;
                EndDate = null;
            }));
        }

        if (!string.IsNullOrWhiteSpace(SearchTerm))
            yield return new FilterChip($"Search “{SearchTerm.Trim()}”", "Clear the search", () => SearchTerm = string.Empty);

        if (StartHour != 0 || EndHour != 23)
        {
            yield return new FilterChip($"{StartHour:00}:00-{EndHour:00}:59", "Include every hour", () => Batch(() =>
            {
                StartHour = 0;
                EndHour = 23;
            }));
        }

        var days = Days.Where(d => d.IsSelected).Select(d => d.Label).ToList();
        if (days.Count < Days.Length)
        {
            yield return new FilterChip(days.Count == 0 ? "No days selected" : string.Join(", ", days),
                "Include every day of the week", () => Batch(() =>
                {
                    foreach (var day in Days)
                        day.IsSelected = true;
                }));
        }

        if (ExcludedArtists.Count > 0)
        {
            yield return new FilterChip(
                $"{ExcludedArtists.Count} artist{(ExcludedArtists.Count == 1 ? "" : "s")} excluded",
                "Stop excluding " + string.Join(", ", ExcludedArtists), ExcludedArtists.Clear);
        }

        if (ExcludedTracks.Count > 0)
        {
            yield return new FilterChip(
                $"{ExcludedTracks.Count} track{(ExcludedTracks.Count == 1 ? "" : "s")} excluded",
                "Stop excluding " + string.Join(", ", ExcludedTracks), ExcludedTracks.Clear);
        }

        if (ModeChip(ShuffleMode, "Shuffled plays only", "No shuffled plays", "Count shuffled plays again",
                () => ShuffleMode = PlaybackMode.Any) is { } shuffle)
            yield return shuffle;
        if (ModeChip(OfflineMode, "Offline plays only", "No offline plays", "Count offline plays again",
                () => OfflineMode = PlaybackMode.Any) is { } offline)
            yield return offline;
        if (ModeChip(PrivateMode, "Private sessions only", "No private sessions", "Count private sessions again",
                () => PrivateMode = PlaybackMode.Any) is { } incognito)
            yield return incognito;

        if (_excludedDevices.Count > 0)
        {
            yield return new FilterChip(
                $"{_excludedDevices.Count} device{(_excludedDevices.Count == 1 ? "" : "s")} left out",
                "Count plays on " + string.Join(", ", _excludedDevices.Order()) + " again",
                () => IncludeEvery(_excludedDevices));
        }

        if (_excludedCountries.Count > 0)
        {
            yield return new FilterChip(
                $"{_excludedCountries.Count} countr{(_excludedCountries.Count == 1 ? "y" : "ies")} left out",
                "Count plays from " + string.Join(", ", _excludedCountries.Order()) + " again",
                () => IncludeEvery(_excludedCountries));
        }
    }

    private static FilterChip? ModeChip(PlaybackMode mode, string only, string exclude, string clearHint, Action clear) => mode switch
    {
        PlaybackMode.Only => new FilterChip(only, clearHint, clear),
        PlaybackMode.Exclude => new FilterChip(exclude, clearHint, clear),
        _ => null,
    };

    // ---- Date ranges -------------------------------------------------------------------------

    /// <summary>
    /// Narrows to one hour of one day of the week (0=Sunday), as one change: what a click on
    /// a heatmap square asks for.
    /// </summary>
    public void SetSlot(int dayOfWeek, int hour) => Batch(() =>
    {
        foreach (var day in Days)
            day.IsSelected = day.Index == dayOfWeek;
        StartHour = Math.Clamp(hour, 0, 23);
        EndHour = Math.Clamp(hour, 0, 23);
    });

    /// <summary>Sets both ends of the date range as one change.</summary>
    public void SetRange(DateTime? start, DateTime? end) => Batch(() =>
    {
        StartDate = start;
        EndDate = end;
    });

    /// <summary>
    /// Offers the quick ranges that make sense for the loaded history: the recent ones count
    /// back from <paramref name="lastListen"/>, and there is one per year it covers.
    /// </summary>
    public void SetAvailableDates(DateTime? lastListen, IEnumerable<int> years)
    {
        QuickRanges.Clear();
        foreach (var option in DateRangeOption.For(lastListen, years))
            QuickRanges.Add(option);
        HasQuickRanges = QuickRanges.Count > 0;
        RefreshQuickRanges();
    }

    [RelayCommand]
    private void ApplyQuickRange(DateRangeOption? option)
    {
        if (option is not null)
            SetRange(option.Start, option.End);
    }

    private void RefreshQuickRanges()
    {
        foreach (var option in QuickRanges)
            option.IsActive = option.Matches(StartDate, EndDate);
    }

    /// <summary>Makes several filter changes and raises them as one.</summary>
    private void Batch(Action change)
    {
        _suppress = true;
        try { change(); }
        finally { _suppress = false; }
        Raise();
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

    /// <summary>Excludes several artists as one change, so the results are worked out once.</summary>
    public void ExcludeArtists(IEnumerable<string> names) => Batch(() =>
    {
        foreach (var name in names)
            ExcludeArtist(name);
    });

    /// <summary>Excludes several tracks as one change, so the results are worked out once.</summary>
    public void ExcludeTracks(IEnumerable<string> names) => Batch(() =>
    {
        foreach (var name in names)
            ExcludeTrack(name);
    });

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
        ShuffleMode = PlaybackMode.Any;
        OfflineMode = PlaybackMode.Any;
        PrivateMode = PlaybackMode.Any;
        _excludedDevices.Clear();
        _excludedCountries.Clear();
        SyncChoiceToggles();
        _suppress = false;
        Raise();
    }
}

/// <summary>One option in a shuffle, offline or private-session picker.</summary>
public sealed record PlaybackChoice(PlaybackMode Mode, string Label)
{
    public static IReadOnlyList<PlaybackChoice> For(string plural, string capitalised) => new[]
    {
        new PlaybackChoice(PlaybackMode.Any, "Count every play"),
        new PlaybackChoice(PlaybackMode.Only, $"Only {plural}"),
        new PlaybackChoice(PlaybackMode.Exclude, $"{capitalised} left out"),
    };

    public override string ToString() => Label;
}

/// <summary>A checkbox for one device family or country: ticked means its plays count.</summary>
public sealed partial class ChoiceToggle : ObservableObject
{
    private readonly Action<ChoiceToggle> _changed;
    private bool _quiet;

    public ChoiceToggle(string key, string label, string description, bool isSelected, Action<ChoiceToggle> changed)
    {
        Key = key;
        Label = label;
        Description = description;
        _isSelected = isSelected;
        _changed = changed;
    }

    public string Key { get; }
    public string Label { get; }

    /// <summary>The full name, for the tooltip and screen readers when the label is a code.</summary>
    public string Description { get; }

    [ObservableProperty] private bool _isSelected;

    /// <summary>Updates the tick without reporting it as a change of filter.</summary>
    internal void SetQuietly(bool value)
    {
        _quiet = true;
        try { IsSelected = value; }
        finally { _quiet = false; }
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (!_quiet)
            _changed(this);
    }

    public override string ToString() => Label;
}

/// <summary>One active filter in the chip row, with a way to clear just that filter.</summary>
public sealed class FilterChip
{
    public FilterChip(string text, string clearHint, Action clear)
    {
        Text = text;
        ClearHint = clearHint;
        ClearCommand = new RelayCommand(clear);
    }

    public string Text { get; }

    /// <summary>What the chip's clear button does, for its tooltip and screen readers.</summary>
    public string ClearHint { get; }

    public IRelayCommand ClearCommand { get; }

    public override string ToString() => Text;
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
