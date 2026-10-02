using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Input;
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

    public DetailWindow(DetailResult detail)
    {
        InitializeComponent();
        _detail = detail;

        TitleText.Text = detail.Title;
        SubtitleText.Text = detail.Subtitle.Length > 0
            ? $"{detail.Scope} - {detail.Subtitle}"
            : detail.Scope.ToString();
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
        // grid underneath would just repeat it.
        if (detail.Scope == DetailScope.Track)
            TracksCard.Visibility = Visibility.Collapsed;
        if (detail.Scope == DetailScope.Album)
            AlbumsCard.Visibility = Visibility.Collapsed;
        if (detail.Artists.Count == 0)
            ArtistsCard.Visibility = Visibility.Collapsed;

        // Spotify has nothing to open for a whole year.
        if (detail.WebUrl.Length == 0)
            OpenButton.Visibility = Visibility.Collapsed;
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
