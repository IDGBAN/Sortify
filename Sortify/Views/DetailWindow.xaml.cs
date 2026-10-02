using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Sortify.Models;
using Sortify.Services;

namespace Sortify.Views;

/// <summary>
/// Drill-down view for a single artist, track, album or year. Populated directly rather
/// than through a view model: it is read-only and lives only as long as the dialog.
/// </summary>
public partial class DetailWindow : Window
{
    private readonly DetailResult _detail;

    /// <summary>Builds the breakdown for a row opened from this one; null when it can't drill further.</summary>
    private readonly Func<DetailScope, string, string, Task<DetailResult?>>? _openDetail;

    /// <summary>True while a nested breakdown is being built, so a second double-click can't stack another.</summary>
    private bool _opening;

    public DetailWindow(DetailResult detail) : this(detail, null) { }

    /// <param name="openDetail">
    /// Builds the breakdown for a track, album, artist or episode double-clicked in this
    /// window, under the same filters. Without it the rows are display only.
    /// </param>
    public DetailWindow(DetailResult detail, Func<DetailScope, string, string, Task<DetailResult?>>? openDetail)
    {
        InitializeComponent();
        _detail = detail;
        _openDetail = openDetail;

        TitleText.Text = detail.Title;
        SubtitleText.Inlines.Add(new Run(ScopeLabel(detail)));
        if (detail.Subtitle.Length > 0)
        {
            SubtitleText.Inlines.Add(new Run(" - "));
            if (openDetail is not null && detail.Scope is DetailScope.Track or DetailScope.Album or DetailScope.Episode)
            {
                var link = new Hyperlink(new Run(detail.Subtitle))
                {
                    ToolTip = $"Open the breakdown for {detail.Subtitle}",
                };
                link.Click += OnSubtitleLinkClick;
                SubtitleText.Inlines.Add(link);
            }
            else
            {
                SubtitleText.Inlines.Add(new Run(detail.Subtitle));
            }
        }

        if (openDetail is null)
        {
            ArtistsHint.Visibility = Visibility.Collapsed;
            TracksHint.Visibility = Visibility.Collapsed;
            AlbumsHint.Visibility = Visibility.Collapsed;
        }

        Title = detail.Subtitle.Length > 0
            ? $"{detail.Title} - {detail.Subtitle}"
            : detail.Title;

        TotalTimeText.Text = TimeFormat.Friendly(detail.TotalTime);
        PlaysText.Text = detail.PlayCount.ToString("N0");
        ActiveDaysText.Text = detail.ActiveDays.ToString("N0");
        FirstPlayedText.Text = detail.FirstPlayed is { } f ? TimeFormat.Timestamp(f) : "-";
        LastPlayedText.Text = detail.LastPlayed is { } l ? TimeFormat.Timestamp(l) : "-";

        var month = ChartBuilder.DetailByMonth(detail);
        MonthChart.Series = month.Series;
        MonthChart.XAxes = month.XAxes;
        MonthChart.YAxes = month.YAxes;

        var hour = ChartBuilder.DetailByHour(detail);
        HourChart.Series = hour.Series;
        HourChart.XAxes = hour.XAxes;
        HourChart.YAxes = hour.YAxes;

        TracksGrid.ItemsSource = detail.Tracks;
        AlbumsGrid.ItemsSource = detail.Albums;
        ArtistsGrid.ItemsSource = detail.Artists;

        // Opening a single track already shows that one track in the header; the one-row
        // grid underneath would just repeat it. A show's "albums" are only the show itself.
        if (detail.Scope is DetailScope.Track or DetailScope.Episode)
            TracksCard.Visibility = Visibility.Collapsed;
        if (detail.Scope is DetailScope.Album or DetailScope.Show or DetailScope.Episode)
            AlbumsCard.Visibility = Visibility.Collapsed;
        if (detail.Artists.Count == 0)
            ArtistsCard.Visibility = Visibility.Collapsed;
        if (detail.Scope == DetailScope.Show)
        {
            bool book = detail.Kind == ContentKind.Audiobook;
            TracksHeader.Text = book ? "Chapters" : "Episodes";
            TracksGrid.Columns[0].Header = book ? "Chapter" : "Episode";
            TracksGrid.Columns[1].Header = book ? "Audiobook" : "Show";
        }

        // Spotify has nothing to open for a whole year.
        if (detail.WebUrl.Length == 0)
            OpenButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>"Podcast", "Chapter" and so on: what the header says this breakdown is of.</summary>
    internal static string ScopeLabel(DetailResult detail) => detail.Scope switch
    {
        DetailScope.Show => detail.Kind == ContentKind.Audiobook ? "Audiobook" : "Podcast",
        DetailScope.Episode => detail.Kind == ContentKind.Audiobook ? "Chapter" : "Episode",
        _ => detail.Scope.ToString(),
    };

    /// <summary>What a row in one of the grids opens, or null for a row that opens nothing.</summary>
    internal static (DetailScope Scope, string Title, string Subtitle)? TargetFor(object item) => item switch
    {
        TrackStat t => (DetailScope.Track, t.Track, t.Artist),
        ArtistStat a => (DetailScope.Artist, a.Artist, string.Empty),
        AlbumStat a => (DetailScope.Album, a.Album, a.Artist),
        YearStat y => (DetailScope.Year, y.Year.ToString(CultureInfo.InvariantCulture), string.Empty),
        ShowStat s => (DetailScope.Show, s.Show, string.Empty),
        EpisodeStat e => (DetailScope.Episode, e.Episode, e.Show),
        SkippedTrackStat s => (DetailScope.Track, s.Track, s.Artist),
        MonthStat m => (DetailScope.Artist, m.TopArtist, string.Empty),
        _ => null,
    };

    /// <summary>
    /// What a row opens from inside this breakdown. A show lists its episodes in the tracks
    /// grid, and those open as episodes rather than as tracks.
    /// </summary>
    private (DetailScope Scope, string Title, string Subtitle)? TargetFrom(object item) =>
        _detail.Scope == DetailScope.Show && item is TrackStat t
            ? (DetailScope.Episode, t.Track, t.Artist)
            : TargetFor(item);

    private async void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Double-clicks on the header or scrollbar land here too; only a row opens anything.
        if (FindRow(e.OriginalSource as DependencyObject) is not { } row)
            return;

        e.Handled = true;
        await OpenAsync(row.Item);
    }

    private async void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not DataGrid { SelectedItem: { } item })
            return;

        e.Handled = true;
        await OpenAsync(item);
    }

    /// <summary>The name under the title: a track's or album's artist, or an episode's show.</summary>
    private async void OnSubtitleLinkClick(object sender, RoutedEventArgs e)
    {
        if (_detail.Subtitle.Length == 0)
            return;

        await OpenAsync(_detail.Scope == DetailScope.Episode
            ? new ShowStat { Show = _detail.Subtitle }
            : new ArtistStat { Artist = _detail.Subtitle });
    }

    private async Task OpenAsync(object item)
    {
        if (_opening || _openDetail is null || TargetFrom(item) is not { } target)
            return;

        DetailResult? detail;
        _opening = true;
        Mouse.OverrideCursor = Cursors.AppStarting;
        try
        {
            detail = await _openDetail(target.Scope, target.Title, target.Subtitle);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            _opening = false;
        }

        if (detail is not { PlayCount: > 0 })
            return;

        new DetailWindow(detail, _openDetail) { Owner = this }.ShowDialog();
    }

    private static DataGridRow? FindRow(DependencyObject? d)
    {
        while (d is not null and not DataGridRow)
        {
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return d as DataGridRow;
    }

    /// <summary>Escape closes the dialog, as it would any other modal.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void OnOpenInSpotify(object sender, RoutedEventArgs e)
    {
        try
        {
            // UseShellExecute hands the URL to the default browser (or the Spotify app,
            // when it has registered itself as the handler).
            Process.Start(new ProcessStartInfo(_detail.WebUrl) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No handler registered, or the shell refused; nothing useful to recover to.
            MessageBox.Show(this, "Could not open a browser for that link.", "Sortify",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>Copies the header figures as plain text, for pasting somewhere else.</summary>
    private void OnCopySummary(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine(_detail.Subtitle.Length > 0
            ? $"{_detail.Title} - {_detail.Subtitle}"
            : _detail.Title);
        sb.AppendLine($"Time listened: {TimeFormat.Friendly(_detail.TotalTime)}");
        sb.AppendLine($"Plays: {_detail.PlayCount:N0}");
        sb.AppendLine($"Active days: {_detail.ActiveDays:N0}");
        if (_detail.FirstPlayed is { } first)
            sb.AppendLine($"First played: {TimeFormat.Timestamp(first)}");
        if (_detail.LastPlayed is { } last)
            sb.AppendLine($"Last played: {TimeFormat.Timestamp(last)}");

        try
        {
            Clipboard.SetDataObject(sb.ToString());
        }
        catch (Exception)
        {
            // The clipboard can be locked by another process; copying is best-effort.
        }
    }
}
