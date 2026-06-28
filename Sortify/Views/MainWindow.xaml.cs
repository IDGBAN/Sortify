using System.Windows;
using System.Windows.Controls;
using Sortify.ViewModels;

namespace Sortify.Views;

public partial class MainWindow : Window
{
    // Preload the next page well before the user reaches the very bottom: trigger once
    // they're within this many viewport-heights of the end (with a small px floor for
    // short charts).
    private const double PreloadViewports = 1.5;
    private const double MinPreloadPx = 120;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void OnTracksScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (NearBottom(sender))
            (DataContext as MainViewModel)?.LoadMoreTracks();
    }

    private void OnArtistsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (NearBottom(sender))
            (DataContext as MainViewModel)?.LoadMoreArtists();
    }

    private static bool NearBottom(object sender)
    {
        if (sender is not ScrollViewer sv || sv.ScrollableHeight <= 0)
            return false;
        double threshold = Math.Max(MinPreloadPx, sv.ViewportHeight * PreloadViewports);
        return sv.VerticalOffset >= sv.ScrollableHeight - threshold;
    }
}
