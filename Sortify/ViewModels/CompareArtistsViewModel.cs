using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sortify.Models;
using Sortify.Services;

namespace Sortify.ViewModels;

/// <summary>
/// Compare Artists on the Trends tab: a few chosen artists' listening on one timeline, picked
/// by typing a name. It starts with the top artists of each history loaded, and after that
/// only changes when the user changes it.
/// </summary>
public sealed partial class CompareArtistsViewModel : ObservableObject
{
    /// <summary>What the chart is drawn from: the plays, the sidebar's filters and the day weeks start on.</summary>
    public readonly record struct Source(IReadOnlyList<PlayRecord> Records, FilterOptions Filter, DayOfWeek FirstDayOfWeek);

    /// <summary>How many of the top artists the chart starts with after a history loads.</summary>
    private const int ArtistsToStartWith = 3;

    /// <summary>How many name suggestions show under the box while typing.</summary>
    private const int SuggestionCount = 6;

    private readonly Func<Source?> _source;
    private IReadOnlyList<ArtistStat> _known = Array.Empty<ArtistStat>();
    private ArtistTimeline _timeline = ArtistTimeline.Empty;
    private ChartBuilder.TimeGranularity _granularity = ChartBuilder.TimeGranularity.Monthly;
    private CancellationTokenSource? _cts;
    private bool _seeded;

    public CompareArtistsViewModel(Func<Source?> source) => _source = source;

    /// <summary>A line for the status bar, when an artist can't be added.</summary>
    public event EventHandler<string>? Notice;

    /// <summary>The artists on the chart, in the order their lines are coloured.</summary>
    public ObservableCollection<string> Artists { get; } = new();

    [ObservableProperty] private string _query = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestionDetails))]
    private IReadOnlyList<string> _suggestions = Array.Empty<string>();

    [ObservableProperty] private ChartData _chart = ChartData.Empty;
    [ObservableProperty] private string _hint = string.Empty;

    /// <summary>Whether the lines show listening time or plays; follows the Time / Plays switch.</summary>
    [ObservableProperty] private Metric _metric;

    /// <summary>The suggested artists with their listening under the current filters, for the list under the box.</summary>
    public IReadOnlyList<SearchSuggestion> SuggestionDetails => Suggestions
        .Select(name => _known.First(a => a.Artist == name))
        .Select(a => new SearchSuggestion("Artist", a.Artist, "", a.TotalMsPlayed, a.PlayCount, "Current filters"))
        .ToArray();

    partial void OnQueryChanged(string value) => RefreshSuggestions();

    partial void OnMetricChanged(Metric value) => Redraw();

    /// <summary>A different history is loaded, so the next results pick the artists again.</summary>
    public void StartOver() => _seeded = false;

    /// <summary>
    /// Takes the artists of a new analysis, ranked as the Time / Plays switch ranks them: the
    /// ones suggested while typing, and the first few to start a fresh history with. Then
    /// redraws under the current filters.
    /// </summary>
    public async Task UpdateAsync(IReadOnlyList<ArtistStat> artists)
    {
        _known = artists;
        if (!_seeded)
        {
            _seeded = true;
            Artists.Clear();
            foreach (var artist in artists.Take(ArtistsToStartWith))
                Artists.Add(artist.Artist);
        }

        RefreshSuggestions();
        await RefreshAsync();
    }

    /// <summary>Draws the chart again from the same numbers, for a new theme's colours.</summary>
    public void Redraw() => Chart = ChartBuilder.ArtistTimelines(_timeline, _granularity, Metric);

    /// <summary>Switches between monthly and weekly buckets.</summary>
    public async Task SetGranularityAsync(ChartBuilder.TimeGranularity granularity)
    {
        if (_granularity == granularity)
            return;
        _granularity = granularity;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task AddAsync(string? artist)
    {
        artist ??= Suggestions.FirstOrDefault();
        if (string.IsNullOrEmpty(artist) || Artists.Contains(artist))
            return;
        if (Artists.Count >= ArtistComparison.MaxArtists)
        {
            RefreshHint();
            Notice?.Invoke(this, $"The comparison chart holds {ArtistComparison.MaxArtists} artists at most. Remove one to add {artist}.");
            return;
        }

        Artists.Add(artist);
        Query = string.Empty;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RemoveAsync(string? artist)
    {
        if (artist is null || !Artists.Remove(artist))
            return;
        RefreshSuggestions();
        await RefreshAsync();
    }

    private void RefreshSuggestions()
    {
        string query = Query.Trim();
        Suggestions = query.Length == 0
            ? Array.Empty<string>()
            : _known
                .Where(a => !Artists.Contains(a.Artist))
                .Select(a => (a.Artist, Rank: NameMatch.Rank(a.Artist, query), a.TotalMsPlayed))
                .Where(x => x.Rank >= 0)
                .OrderBy(x => x.Rank)
                .ThenByDescending(x => x.TotalMsPlayed)
                .Take(SuggestionCount)
                .Select(x => x.Artist)
                .ToList();
        RefreshHint();
    }

    private void RefreshHint()
    {
        Hint = Artists.Count == 0
            ? $"Type an artist's name and pick from the matches to add them. Up to {ArtistComparison.MaxArtists} at once."
            : Query.Trim().Length > 0 && Suggestions.Count == 0
                ? Artists.Count >= ArtistComparison.MaxArtists
                    ? $"The chart already has {ArtistComparison.MaxArtists} artists. Remove one to add another."
                    : $"No artist in the current results matches “{Query.Trim()}”."
                : string.Empty;
    }

    /// <summary>Rebuilds the timeline under the current filters; a newer call cancels an older one.</summary>
    private async Task RefreshAsync()
    {
        RefreshHint();
        var previous = _cts;
        _cts = new CancellationTokenSource();
        previous?.Cancel();
        var token = _cts.Token;

        try
        {
            var timeline = _source() is { } source && Artists.Count > 0
                ? await ArtistComparison.BuildAsync(source.Records, source.Filter, Artists.ToList(),
                    _granularity, source.FirstDayOfWeek, token)
                : ArtistTimeline.Empty;
            if (token.IsCancellationRequested)
                return;
            _timeline = timeline;
            Redraw();
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
}
