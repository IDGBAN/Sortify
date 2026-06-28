using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
