using CommunityToolkit.Mvvm.ComponentModel;
using Sortify.Models;
using Sortify.Services;

namespace Sortify.ViewModels;

/// <summary>
/// The History tab: every play that passes the sidebar filters, newest first. Like the Compare
/// tab, it only builds its rows while it is on screen and picks up changes when shown again.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    /// <summary>What the tab lists: the plays, and the filters the rest of the window was worked out under.</summary>
    public readonly record struct Source(IReadOnlyList<PlayRecord> Records, FilterOptions Filter);

    private readonly Func<Source?> _source;
    private CancellationTokenSource? _cts;
    private bool _stale = true;

    /// <summary>Every row under the sidebar filters, in the order the table is sorted.</summary>
    private IReadOnlyList<PlayRow> _all = Array.Empty<PlayRow>();

    public HistoryViewModel(Func<Source?> source) => _source = source;

    /// <summary>The rows on screen: <see cref="_all"/> narrowed by the row filter.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGathering))]
    [NotifyPropertyChangedFor(nameof(IsRefreshing))]
    private IReadOnlyList<PlayRow> _rows = Array.Empty<PlayRow>();

    [ObservableProperty] private string _quickFilter = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _emptyText = string.Empty;

    /// <summary>True while rows are being gathered or sorted off the UI thread.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGathering))]
    [NotifyPropertyChangedFor(nameof(IsRefreshing))]
    private bool _isWorking;

    /// <summary>The first gathering, while there is nothing to show yet.</summary>
    public bool IsGathering => IsWorking && Rows.Count == 0;

    /// <summary>Gathering again or sorting, with the old rows still on screen.</summary>
    public bool IsRefreshing => IsWorking && Rows.Count > 0;

    /// <summary>The day the table was last asked to jump to.</summary>
    [ObservableProperty] private DateTime? _goToDate;

    /// <summary>Says where a jump landed when the day itself had no plays; empty otherwise.</summary>
    [ObservableProperty] private string _jumpNote = string.Empty;

    /// <summary>Set by the window as the History tab comes and goes.</summary>
    [ObservableProperty] private bool _isVisible;

    /// <summary>The <see cref="PlayRow"/> property the rows are sorted by.</summary>
    public string SortColumn { get; private set; } = nameof(PlayRow.When);

    public bool SortDescending { get; private set; } = true;

    /// <summary>Asks the table to bring a row to the top of the view and select it.</summary>
    public event EventHandler<PlayRow>? ScrollRequested;

    /// <summary>Raised when the rows go back to newest first by themselves, so the headers can follow.</summary>
    public event EventHandler? SortReset;

    partial void OnIsVisibleChanged(bool value)
    {
        if (value && _stale)
            _ = UpdateAsync();
    }

    /// <summary>The sidebar or the loaded history changed, so the rows are out of date.</summary>
    public void Invalidate()
    {
        _stale = true;
        if (IsVisible)
            _ = UpdateAsync();
    }

    partial void OnQuickFilterChanged(string value)
    {
        JumpNote = string.Empty;
        ApplyQuickFilter();
    }

    partial void OnGoToDateChanged(DateTime? value)
    {
        if (value is { } day)
            _ = GoToAsync(day.Date);
    }

    /// <summary>
    /// Gathers the rows again if they are out of date, and sorts them by the current column, off
    /// the UI thread. Rebuilding and sorting share one pipeline, so a sort asked for while the
    /// rows are being gathered can't be overtaken by the older order: each call cancels the one
    /// before, and an unfinished rebuild is simply done again.
    /// </summary>
    public async Task UpdateAsync()
    {
        bool rebuild = _stale;
        var source = rebuild ? _source() : null;
        if (rebuild && source is null)
        {
            _all = Array.Empty<PlayRow>();
            ApplyQuickFilter();
            _stale = false;
            return;
        }

        var previous = _cts;
        _cts = new CancellationTokenSource();
        previous?.Cancel();
        var token = _cts.Token;

        string column = SortColumn;
        bool descending = SortDescending;
        var current = _all;
        IsWorking = true;
        try
        {
            var rows = await Task.Run(() => Sorted(
                rebuild ? Build(source!.Value.Records, source.Value.Filter, token) : current,
                column, descending), token);
            if (token.IsCancellationRequested)
                return;

            _all = rows;
            if (rebuild)
            {
                _stale = false;
                JumpNote = string.Empty;
            }
            ApplyQuickFilter();
        }
        catch (OperationCanceledException)
        {
            // A newer call took over.
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsWorking = false;
            previous?.Dispose();
        }
    }

    /// <summary>Every play under the filters, oldest first.</summary>
    internal static List<PlayRow> Build(IReadOnlyList<PlayRecord> records, FilterOptions filter, CancellationToken cancellationToken = default)
    {
        var plays = new List<PlayRecord>();
        int counter = 0;
        foreach (var r in FilterEngine.Apply(records, filter))
        {
            if ((++counter & 0x3FFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            plays.Add(r);
        }

        // Stable, so plays that share a timestamp keep the order their files gave them.
        return RowsFor(plays.OrderBy(r => r.Timestamp).ToList());
    }

    /// <summary>Rows for plays already in time order, oldest first, each with its device family worked out.</summary>
    internal static List<PlayRow> RowsFor(IReadOnlyList<PlayRecord> inTimeOrder)
    {
        var rows = new List<PlayRow>(inTimeOrder.Count);
        for (int i = 0; i < inTimeOrder.Count; i++)
            rows.Add(new PlayRow(inTimeOrder[i], Devices.FamilyOf(inTimeOrder[i].Platform), i));
        return rows;
    }

    /// <summary>Sorts by a column of the table, then shows the result through the row filter.</summary>
    public Task SortAsync(string column, bool descending)
    {
        SortColumn = column;
        SortDescending = descending;
        return UpdateAsync();
    }

    /// <summary>
    /// The rows sorted by <paramref name="column"/>. Ties fall back to time, newest first, so a
    /// sort always comes out the same way. Keys are worked out once per row up front: the
    /// readable end reason, for one, would otherwise be looked up on every comparison.
    /// </summary>
    internal static List<PlayRow> Sorted(IReadOnlyList<PlayRow> rows, string column, bool descending)
    {
        if (column == nameof(PlayRow.When) && InTimeOrder(rows, descending) is { } byTime)
            return byTime;

        bool byText = column is nameof(PlayRow.Track) or nameof(PlayRow.Artist) or nameof(PlayRow.Album)
            or nameof(PlayRow.Ended) or nameof(PlayRow.Device) or nameof(PlayRow.Country) or nameof(PlayRow.Mode);

        var items = rows.ToArray();
        var keys = new SortKey[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            var row = items[i];
            keys[i] = column switch
            {
                nameof(PlayRow.Track) => new SortKey(row.Track, 0, row.Order),
                nameof(PlayRow.Artist) => new SortKey(row.Artist, 0, row.Order),
                nameof(PlayRow.Album) => new SortKey(row.Album, 0, row.Order),
                nameof(PlayRow.Ended) => new SortKey(row.Ended, 0, row.Order),
                nameof(PlayRow.Device) => new SortKey(row.Device, 0, row.Order),
                nameof(PlayRow.Country) => new SortKey(row.Country, 0, row.Order),
                nameof(PlayRow.Mode) => new SortKey(row.Mode, 0, row.Order),
                nameof(PlayRow.Played) => new SortKey(string.Empty, row.Record.MsPlayed, row.Order),
                _ => new SortKey(string.Empty, row.Order, row.Order),
            };
        }

        Array.Sort(keys, items, new SortKeyComparer(byText, descending ? -1 : 1));
        return items.ToList();
    }

    /// <summary>
    /// Time order without a sort: each row knows its place in it. Null when the rows aren't one
    /// whole set from <see cref="Build"/>, whose places run without gaps.
    /// </summary>
    private static List<PlayRow>? InTimeOrder(IReadOnlyList<PlayRow> rows, bool descending)
    {
        var slots = new PlayRow?[rows.Count];
        foreach (var row in rows)
        {
            if ((uint)row.Order >= (uint)slots.Length || slots[row.Order] is not null)
                return null;
            slots[row.Order] = row;
        }
        if (descending)
            Array.Reverse(slots);
        return new List<PlayRow>(slots!);
    }

    private readonly record struct SortKey(string Text, long Number, int Order);

    private sealed class SortKeyComparer : IComparer<SortKey>
    {
        private readonly bool _byText;
        private readonly int _sign;

        public SortKeyComparer(bool byText, int sign)
        {
            _byText = byText;
            _sign = sign;
        }

        public int Compare(SortKey a, SortKey b)
        {
            int c = _byText
                ? string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase)
                : a.Number.CompareTo(b.Number);
            return c != 0 ? _sign * c : b.Order.CompareTo(a.Order);
        }
    }

    private void ApplyQuickFilter()
    {
        string term = QuickFilter.Trim();
        Rows = term.Length == 0 ? _all : _all.Where(r => r.Matches(term)).ToList();

        Summary = _all.Count == 0
            ? string.Empty
            : term.Length == 0
                ? Plural.Of(_all.Count, "play")
                : $"{Rows.Count:N0} of {Plural.Of(_all.Count, "play")}";
        EmptyText = _all.Count == 0
            ? "No plays match the current filters."
            : $"No plays match “{term}”. The row filter looks at track, artist and album names.";
    }

    /// <summary>
    /// Brings the plays of <paramref name="day"/> to the top of the table, putting the rows back
    /// in time order first. A day with no plays lands on the closest earlier one.
    /// </summary>
    internal async Task GoToAsync(DateTime day)
    {
        if (SortColumn != nameof(PlayRow.When) || !SortDescending)
        {
            SortReset?.Invoke(this, EventArgs.Empty);
            await SortAsync(nameof(PlayRow.When), descending: true);
        }
        else if (_stale || IsWorking)
        {
            await UpdateAsync();
        }

        // Another jump or sort may have started while this one waited.
        if (GoToDate?.Date != day || SortColumn != nameof(PlayRow.When) || !SortDescending)
            return;

        if (Rows.Count == 0)
        {
            JumpNote = string.Empty;
            return;
        }

        // Newest first, so the first row that ended by the end of the day is the last play of
        // the day asked for, or of the closest day before it.
        var end = FilterOptions.EndOfDay(day);
        int lo = 0, hi = Rows.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (Rows[mid].Record.Timestamp <= end)
                hi = mid;
            else
                lo = mid + 1;
        }

        string date = TimeFormat.Day(day);
        if (lo == Rows.Count)
        {
            var earliest = Rows[^1];
            JumpNote = $"Nothing as early as {date}: here is the first play.";
            ScrollRequested?.Invoke(this, earliest);
            return;
        }

        var landed = Rows[lo];
        JumpNote = landed.Record.Timestamp.Date == day
            ? string.Empty
            : $"No plays on {date}: here is the last one before it.";
        ScrollRequested?.Invoke(this, landed);
    }
}

/// <summary>One play on the History tab.</summary>
public sealed class PlayRow
{
    internal PlayRow(PlayRecord record, string device, int order)
    {
        Record = record;
        Device = device;
        Order = order;
    }

    public PlayRecord Record { get; }

    /// <summary>Position in time order, oldest first. Breaks ties so every sort repeats exactly.</summary>
    internal int Order { get; }

    /// <summary>When the play ended. Empty for the rare play the export gave no time.</summary>
    public string When => Record.Timestamp == DateTime.MinValue ? string.Empty : TimeFormat.Timestamp(Record.Timestamp);

    public string Track => Record.DisplayTrack;
    public string Artist => Record.DisplayArtist;

    /// <summary>The album, or for a podcast or audiobook (whose show is in the artist column) which it was.</summary>
    public string Album => Record.Kind switch
    {
        ContentKind.Podcast => "Podcast",
        ContentKind.Audiobook => "Audiobook",
        _ => Record.AlbumName,
    };

    public TimeSpan Played => TimeSpan.FromMilliseconds(Record.MsPlayed);

    /// <summary>Why the play ended, in words. Empty when the export doesn't say.</summary>
    public string Ended => Record.ReasonEnd is { Length: > 0 } reason ? EndReasons.Label(reason) : string.Empty;

    /// <summary>The device family, as the Insights tab groups them. Empty when the export doesn't say.</summary>
    public string Device { get; }

    public string Country => Record.Country;

    /// <summary>"Shuffle", "Offline" and "Private" for whichever applied, or empty.</summary>
    public string Mode => Modes[(Record.Shuffle ? 1 : 0) | (Record.Offline ? 2 : 0) | (Record.Incognito ? 4 : 0)];

    private static readonly string[] Modes = Enumerable.Range(0, 8)
        .Select(flags => string.Join(", ", new[] { (1, "Shuffle"), (2, "Offline"), (4, "Private") }
            .Where(m => (flags & m.Item1) != 0)
            .Select(m => m.Item2)))
        .ToArray();

    internal bool Matches(string term) =>
        Track.Contains(term, StringComparison.OrdinalIgnoreCase)
        || Artist.Contains(term, StringComparison.OrdinalIgnoreCase)
        || Album.Contains(term, StringComparison.OrdinalIgnoreCase);
}
