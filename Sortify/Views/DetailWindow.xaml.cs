using System.Diagnostics;
using System.Windows;
using Sortify.Models;
using Sortify.Services;

namespace Sortify.Views;

/// <summary>
/// Drill-down view for a single artist, track or album. Populated directly rather than
/// through a view model: it is read-only and lives only as long as the dialog.
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

        var (monthSeries, monthX, monthY) = ChartBuilder.DetailByMonth(detail);
        MonthChart.Series = monthSeries;
        MonthChart.XAxes = monthX;
        MonthChart.YAxes = monthY;

        var (hourSeries, hourX, hourY) = ChartBuilder.DetailByHour(detail);
        HourChart.Series = hourSeries;
        HourChart.XAxes = hourX;
        HourChart.YAxes = hourY;

        TracksGrid.ItemsSource = detail.Tracks;
        AlbumsGrid.ItemsSource = detail.Albums;

        // Opening a single track already shows that one track in the header; the one-row
        // grid underneath would just repeat it.
        if (detail.Scope == DetailScope.Track)
            TracksCard.Visibility = Visibility.Collapsed;
        if (detail.Scope == DetailScope.Album)
            AlbumsCard.Visibility = Visibility.Collapsed;
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
}
