using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using Microsoft.Win32;
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

    private readonly AppSettings _settings;

    public MainWindow()
    {
        InitializeComponent();

        // App owns the settings so the dialog, the view model and window placement all
        // read and write one instance.
        _settings = (Application.Current as App)?.Settings ?? AppSettings.Load();
        var vm = new MainViewModel(_settings);
        vm.PropertyChanged += OnViewModelPropertyChanged;
        DataContext = vm;

        RestorePlacement();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The status line is where every load, filter and export reports back. Marking it a
        // live region isn't enough on its own; WPF only announces when the event is raised.
        if (e.PropertyName == nameof(MainViewModel.StatusText))
        {
            UIElementAutomationPeer.CreatePeerForElement(StatusLine)
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    // Reopen last session's folder once the window is up, so the user sees the shell (and
    // the loading status) rather than a blank pause before it appears.
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        if (_settings.LastTabIndex < Tabs.Items.Count)
            Tabs.SelectedIndex = _settings.LastTabIndex;

        if (ViewModel is { } vm)
            await vm.RestoreLastFolderAsync();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SavePlacement();
        _settings.LastTabIndex = Tabs.SelectedIndex;
        _settings.Save();
    }

    // ---- Window placement --------------------------------------------------------------------

    private void RestorePlacement()
    {
        if (_settings.WindowWidth > 0 && _settings.WindowHeight > 0)
        {
            Width = _settings.WindowWidth;
            Height = _settings.WindowHeight;
        }

        // Only honour a saved position that still lands on a connected screen; a monitor
        // that has since been unplugged would otherwise put the window out of reach.
        if (_settings.WindowLeft is { } left && _settings.WindowTop is { } top && IsOnScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }

        if (_settings.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    private static bool IsOnScreen(double left, double top)
    {
        var bounds = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

        // A sliver of title bar is enough to drag the window back into view.
        return bounds.Contains(new Point(left + 60, top + 10));
    }

    private void SavePlacement()
    {
        _settings.WindowMaximized = WindowState == WindowState.Maximized;

        // RestoreBounds holds the un-maximized geometry, which is what we want to restore to.
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
            _settings.WindowLeft = bounds.Left;
            _settings.WindowTop = bounds.Top;
        }
    }

    // ---- Keyboard ----------------------------------------------------------------------------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        // Ctrl+1..8 jump straight to a tab.
        if (ctrl && e.Key is >= Key.D1 and <= Key.D8)
        {
            int index = e.Key - Key.D1;
            if (index < Tabs.Items.Count)
            {
                Tabs.SelectedIndex = index;
                e.Handled = true;
            }
            return;
        }

        if (ctrl && e.Key == Key.F)
        {
            FocusQuickFilter();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && Keyboard.FocusedElement is TextBox { Text.Length: > 0 } box &&
            IsQuickFilter(box))
        {
            box.Clear();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    /// <summary>The row filter belonging to the tab currently on screen, if it has one.</summary>
    private TextBox? CurrentQuickFilter() => Tabs.SelectedIndex switch
    {
        1 => TrackQuickFilter,
        2 => ArtistQuickFilter,
        3 => AlbumQuickFilter,
        5 => ShowQuickFilter,
        _ => null,
    };

    private bool IsQuickFilter(TextBox box) =>
        ReferenceEquals(box, TrackQuickFilter) || ReferenceEquals(box, ArtistQuickFilter) ||
        ReferenceEquals(box, AlbumQuickFilter) || ReferenceEquals(box, ShowQuickFilter);

    private void FocusQuickFilter()
    {
        if (CurrentQuickFilter() is { } box)
        {
            box.Focus();
            box.SelectAll();
        }
        else
        {
            // Tabs without a row filter fall back to the global search in the sidebar.
            ViewModel?.SetStatus("This tab has no row filter - use Search in the sidebar instead.");
        }
    }

    // ---- Tabs --------------------------------------------------------------------------------

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

    // ---- Toolbar menus -------------------------------------------------------------------------

    /// <summary>Opens a button's own menu on a left click, so it behaves like a dropdown.</summary>
    private void OnDropdownClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
            return;

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>Built on demand because the recent list changes as folders are opened.</summary>
    private void OnRecentClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || ViewModel is not { } vm)
            return;

        var menu = new ContextMenu
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom,
        };

        if (vm.RecentFolders.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No folders opened yet", IsEnabled = false });
        }
        else
        {
            foreach (var folder in vm.RecentFolders)
            {
                menu.Items.Add(new MenuItem
                {
                    // The full path is often too long for a menu; keep it in the tooltip.
                    Header = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name
                        ? name
                        : folder,
                    ToolTip = folder,
                    Command = vm.OpenFolderPathCommand,
                    CommandParameter = folder,
                });
            }
        }

        menu.IsOpen = true;
    }

    private async void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        dialog.ShowDialog();

        if (dialog.NeedsRefresh && ViewModel is { } vm)
            await vm.ApplySettingsChangesAsync();
    }

    // ---- Chart images ---------------------------------------------------------------------------

    /// <summary>The card a chart context menu was opened on.</summary>
    private static FrameworkElement? CardFor(object sender) =>
        sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement card } } ? card : null;

    private void OnCopyChartImage(object sender, RoutedEventArgs e)
    {
        if (CardFor(sender) is not { } card)
            return;

        try
        {
            bool copied = ImageExporter.CopyToClipboard(card, CardBackground());
            ViewModel?.SetStatus(copied ? "Chart copied to the clipboard." : "There was nothing to copy.", !copied);
        }
        catch (Exception ex)
        {
            // The clipboard can be locked by another process.
            ViewModel?.SetStatus($"Could not copy that chart: {ex.Message}", isError: true);
        }
    }

    private void OnSaveChartImage(object sender, RoutedEventArgs e)
    {
        if (CardFor(sender) is not { } card)
            return;

        var dialog = new SaveFileDialog
        {
            Filter = "PNG images (*.png)|*.png",
            DefaultExt = ".png",
            FileName = "Sortify_chart.png",
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            bool saved = ImageExporter.SavePng(card, CardBackground(), dialog.FileName);
            ViewModel?.SetStatus(
                saved ? $"Saved chart to {dialog.FileName}" : "There was nothing to save.", !saved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ViewModel?.SetStatus($"Could not save that image: {ex.Message}", isError: true);
        }
    }

    /// <summary>Charts draw on a transparent background, so the PNG needs the card colour behind it.</summary>
    private Brush CardBackground() => TryFindResource("PanelBrush") as Brush ?? Brushes.White;

    // ---- Infinite scroll -------------------------------------------------------------------------

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

    private static bool NearBottom(object sender)
    {
        if (sender is not ScrollViewer sv || sv.ScrollableHeight <= 0)
            return false;
        double threshold = Math.Max(MinPreloadPx, sv.ViewportHeight * PreloadViewports);
        return sv.VerticalOffset >= sv.ScrollableHeight - threshold;
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

    /// <summary>The dropped folder, when exactly one was dropped, so it can be remembered.</summary>
    private static string? DroppedFolder(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] items)
            return null;

        var folders = items.Where(Directory.Exists).ToList();
        return folders.Count == 1 ? folders[0] : null;
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
        if (files.Length == 0 || ViewModel is not { } vm)
            return;

        await vm.LoadFilesAsync(files, DroppedFolder(e));
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

    /// <summary>True while a breakdown is being built, so a second double-click can't stack another.</summary>
    private bool _openingDetail;

    private async void OnGridRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // MouseDoubleClick fires for the whole grid, headers and scrollbar included; only a
        // double-click that lands on a row should open anything, and it should open that row
        // rather than whatever happened to be selected before.
        if (FindParent<DataGridRow>(e.OriginalSource as DependencyObject) is not { } row)
            return;

        e.Handled = true;
        await ShowDetailAsync(row.Item);
    }

    /// <summary>Enter opens the selected row, so the breakdown doesn't need a mouse.</summary>
    private async void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not DataGrid { SelectedItem: { } item })
            return;

        // Handled either way: left alone, the grid treats Enter as "move to the next row".
        e.Handled = true;
        await ShowDetailAsync(item);
    }

    private static (DetailScope Scope, string Title, string Subtitle)? DetailTarget(object item) => item switch
    {
        TrackStat t => (DetailScope.Track, t.Track, t.Artist),
        ArtistStat a => (DetailScope.Artist, a.Artist, string.Empty),
        AlbumStat a => (DetailScope.Album, a.Album, a.Artist),
        YearStat y => (DetailScope.Year, y.Year.ToString(CultureInfo.InvariantCulture), string.Empty),
        _ => null,
    };

    private async Task ShowDetailAsync(object item)
    {
        if (_openingDetail || ViewModel is not { } vm || DetailTarget(item) is not { } target)
            return;

        DetailResult? detail;
        _openingDetail = true;
        Mouse.OverrideCursor = Cursors.AppStarting;
        try
        {
            detail = await vm.BuildDetailAsync(target.Scope, target.Title, target.Subtitle);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            _openingDetail = false;
        }

        if (detail is null)
            return;

        if (detail.PlayCount == 0)
        {
            vm.SetStatus($"No plays match \"{target.Title}\" under the current filters.");
            return;
        }

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
}
