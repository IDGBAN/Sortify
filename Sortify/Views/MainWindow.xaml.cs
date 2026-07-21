using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using Sortify.Models;
using Sortify.Services;
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
        Loaded += OnLoaded;
    }

    // Reopen last session's folder once the window is up, so the user sees the shell (and
    // the loading status) rather than a blank pause before it appears.
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (ViewModel is { } vm)
            await vm.RestoreLastFolderAsync();
    }

    // Fade + slide the tab body in whenever the user switches tabs. Filtered to the
    // TabControl's own selection so inner selectors (e.g. clicking a DataGrid row,
    // whose SelectionChanged bubbles up) don't re-trigger the transition.
    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender))
            return;

        if (sender is not TabControl tabs)
            return;

        var host = FindContentHost(tabs);
        if (host is null)
            return;

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220))) { EasingFunction = ease };
        host.BeginAnimation(UIElement.OpacityProperty, fade);

        // The transform supplied by the control template is frozen, so swap in a fresh
        // (mutable) one before animating it.
        if (host.RenderTransform is not TranslateTransform slide || slide.IsFrozen)
        {
            slide = new TranslateTransform();
            host.RenderTransform = slide;
            host.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var move = new DoubleAnimation(10, 0, new Duration(TimeSpan.FromMilliseconds(260))) { EasingFunction = ease };
        slide.BeginAnimation(TranslateTransform.YProperty, move);
    }

    private static FrameworkElement? FindContentHost(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ContentPresenter { Name: "PART_SelectedContentHost" } host)
                return host;

            var found = FindContentHost(child);
            if (found is not null)
                return found;
        }
        return null;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnTracksScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (NearBottom(sender))
            ViewModel?.LoadMoreTracks();
    }

    private void OnArtistsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (NearBottom(sender))
            ViewModel?.LoadMoreArtists();
    }

    private void OnAlbumsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (NearBottom(sender))
            ViewModel?.LoadMoreAlbums();
    }

    // ---- Drag & drop of history JSON files (or folders containing them) ---------------------

    private static string[] DroppedJsonFiles(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] items)
            return Array.Empty<string>();

        var files = new List<string>();
        foreach (var item in items)
        {
            if (Directory.Exists(item))
                files.AddRange(HistoryParser.FindHistoryFiles(item));
            else if (string.Equals(Path.GetExtension(item), ".json", StringComparison.OrdinalIgnoreCase))
                files.Add(item);
        }
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        // DragOver fires continuously while hovering, so keep this check cheap:
        // accept folders and .json files without scanning folder contents yet.
        bool accept = e.Data.GetData(DataFormats.FileDrop) is string[] items &&
                      items.Any(i => Directory.Exists(i) ||
                                     string.Equals(Path.GetExtension(i), ".json", StringComparison.OrdinalIgnoreCase));
        e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnFileDrop(object sender, DragEventArgs e)
    {
        var files = DroppedJsonFiles(e);
        if (files.Length > 0 && ViewModel is { } vm)
            await vm.LoadFilesAsync(files);
    }

    // ---- Grid context menus ----------------------------------------------------------------

    // WPF DataGrids don't select the row under a right-click, so the context menu would act
    // on a stale selection; select it manually before the menu opens.
    private void OnGridRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;

        var row = FindParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is not null)
        {
            row.IsSelected = true;
            grid.SelectedItem = row.Item;
        }
    }

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = d is Visual or Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    private void OnExcludeTrackFromTracks(object sender, RoutedEventArgs e)
    {
        if (TracksGrid.SelectedItem is TrackStat t)
            ViewModel?.ExcludeTrackFromGrid(t.Track);
    }

    private void OnExcludeArtistFromTracks(object sender, RoutedEventArgs e)
    {
        if (TracksGrid.SelectedItem is TrackStat t)
            ViewModel?.ExcludeArtistFromGrid(t.Artist);
    }

    private void OnExcludeArtistFromArtists(object sender, RoutedEventArgs e)
    {
        if (ArtistsGrid.SelectedItem is ArtistStat a)
            ViewModel?.ExcludeArtistFromGrid(a.Artist);
    }

    private void OnExcludeArtistFromAlbums(object sender, RoutedEventArgs e)
    {
        if (AlbumsGrid.SelectedItem is AlbumStat a)
            ViewModel?.ExcludeArtistFromGrid(a.Artist);
    }

    // ---- Drill-down --------------------------------------------------------------------------

    private async void OnTracksGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TracksGrid.SelectedItem is TrackStat t)
            await ShowDetailAsync(DetailScope.Track, t.Track, t.Artist);
    }

    private async void OnArtistsGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ArtistsGrid.SelectedItem is ArtistStat a)
            await ShowDetailAsync(DetailScope.Artist, a.Artist, string.Empty);
    }

    private async void OnAlbumsGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AlbumsGrid.SelectedItem is AlbumStat a)
            await ShowDetailAsync(DetailScope.Album, a.Album, a.Artist);
    }

    private async Task ShowDetailAsync(DetailScope scope, string title, string subtitle)
    {
        if (ViewModel is not { } vm)
            return;

        var detail = await vm.BuildDetailAsync(scope, title, subtitle);
        if (detail is null || detail.PlayCount == 0)
            return;

        new DetailWindow(detail) { Owner = this }.ShowDialog();
    }

    // ---- Copy to clipboard -------------------------------------------------------------------

    private static void TryCopy(string text)
    {
        try
        {
            Clipboard.SetDataObject(text);
        }
        catch (Exception)
        {
            // The clipboard can be locked by another process; copying is best-effort.
        }
    }

    private void OnCopyTrackFromTracks(object sender, RoutedEventArgs e)
    {
        if (TracksGrid.SelectedItem is TrackStat t)
            TryCopy($"{t.Track} - {t.Artist}");
    }

    private void OnCopyArtistFromTracks(object sender, RoutedEventArgs e)
    {
        if (TracksGrid.SelectedItem is TrackStat t)
            TryCopy(t.Artist);
    }

    private void OnCopyArtistFromArtists(object sender, RoutedEventArgs e)
    {
        if (ArtistsGrid.SelectedItem is ArtistStat a)
            TryCopy(a.Artist);
    }

    private void OnCopyAlbumFromAlbums(object sender, RoutedEventArgs e)
    {
        if (AlbumsGrid.SelectedItem is AlbumStat a)
            TryCopy($"{a.Album} - {a.Artist}");
    }

    private void OnCopyArtistFromAlbums(object sender, RoutedEventArgs e)
    {
        if (AlbumsGrid.SelectedItem is AlbumStat a)
            TryCopy(a.Artist);
    }

    // ---- Listening-over-time granularity toggle ---------------------------------------------

    private void OnGranularityChecked(object sender, RoutedEventArgs e)
    {
        // Fires during InitializeComponent for the default-checked button, before the
        // DataContext exists; the view model starts on Daily anyway, so skipping is safe.
        if (ViewModel is not { } vm || sender is not RadioButton { Tag: string tag })
            return;

        if (Enum.TryParse<ChartBuilder.TimeGranularity>(tag, out var granularity))
            vm.SetOverTimeGranularity(granularity);
    }

    private static bool NearBottom(object sender)
    {
        if (sender is not ScrollViewer sv || sv.ScrollableHeight <= 0)
            return false;
        double threshold = Math.Max(MinPreloadPx, sv.ViewportHeight * PreloadViewports);
        return sv.VerticalOffset >= sv.ScrollableHeight - threshold;
    }
}
