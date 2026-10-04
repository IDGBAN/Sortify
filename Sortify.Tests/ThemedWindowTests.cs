using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using Sortify.Models;
using Sortify.Services;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>The title bar every window draws in place of the white one Windows paints.</summary>
[Collection(WpfCollection.Name)]
public class ThemedWindowTests
{
    private static DetailResult SampleDetail() => new()
    {
        Scope = DetailScope.Artist,
        Title = "Band",
        TotalMsPlayed = 120_000,
        PlayCount = 2,
    };

    private static T Part<T>(Window window, string name) where T : FrameworkElement =>
        Assert.IsAssignableFrom<T>(window.Template.FindName(name, window));

    [Fact]
    public void EveryWindowDrawsItsOwnTitleBar()
    {
        WpfTestHost.Run(() =>
        {
            foreach (var window in new ThemedWindow[] { new MainWindow(), new SettingsWindow(new AppSettings()), new DetailWindow(SampleDetail()) })
            {
                window.ApplyTemplate();

                Assert.NotNull(WindowChrome.GetWindowChrome(window));
                Assert.NotNull(window.Icon);
                Assert.Equal(Visibility.Visible, Part<Button>(window, "CloseButton").Visibility);
                Assert.Equal(window.Title, Part<TextBlock>(window, "TitleText").Text);
            }
        });
    }

    [Fact]
    public void OnlyTheMainWindowLeavesItsBrandToTheToolbar()
    {
        WpfTestHost.Run(() =>
        {
            var main = new MainWindow();
            var detail = new DetailWindow(SampleDetail());
            main.ApplyTemplate();
            detail.ApplyTemplate();

            Assert.Equal(Visibility.Collapsed, Part<Image>(main, "TitleIcon").Visibility);
            Assert.Equal(Visibility.Collapsed, Part<TextBlock>(main, "TitleText").Visibility);

            var icon = Part<Image>(detail, "TitleIcon");
            Assert.Equal(Visibility.Visible, icon.Visibility);
            Assert.Same(Application.Current.Resources["SortifyLogo"], icon.Source);
        });
    }

    [Fact]
    public void TheMainToolbarSitsInTheTitleBar()
    {
        WpfTestHost.Run(() =>
        {
            var window = new MainWindow();
            window.ApplyTemplate();
            var chrome = WindowChrome.GetWindowChrome(window);
            var toolbar = Assert.IsAssignableFrom<Panel>(window.TitleBarContent);

            // The taller bar still drags along its whole height.
            Assert.True(window.TitleBarHeight > 32);
            Assert.Equal(window.TitleBarHeight, chrome.ResizeBorderThickness.Top + chrome.CaptionHeight);

            // A logical child of the window, so its bindings reach the view model.
            Assert.Same(window, LogicalTreeHelper.GetParent(toolbar));
            var openFolder = toolbar.Children.OfType<Button>().First(b => Equals(b.Content, "Open Folder"));
            Assert.Same(window.DataContext, openFolder.DataContext);

            // Buttons take clicks; the logo and name let them through to drag the window.
            Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(openFolder));
            Assert.False(toolbar.Children.OfType<Image>().Single().IsHitTestVisible);
            Assert.False(toolbar.Children.OfType<TextBlock>().First().IsHitTestVisible);
        });
    }

    [Fact]
    public void TheDragAreaEndsWhereTheTitleBarDoes()
    {
        WpfTestHost.Run(() =>
        {
            var window = new DetailWindow(SampleDetail());
            window.ApplyTemplate();
            var chrome = WindowChrome.GetWindowChrome(window);

            // WindowChrome counts the caption from below the resize band, so the two together
            // have to cover the drawn bar exactly - no dead strip, no dragging the content.
            Assert.True(chrome.ResizeBorderThickness.Top > 0);
            Assert.Equal(window.TitleBarHeight, chrome.ResizeBorderThickness.Top + chrome.CaptionHeight);
        });
    }

    [Fact]
    public void AFixedSizeWindowOffersOnlyClose()
    {
        WpfTestHost.Run(() =>
        {
            var window = new SettingsWindow(new AppSettings());
            window.ApplyTemplate();
            var chrome = WindowChrome.GetWindowChrome(window);

            Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
            Assert.Equal(Visibility.Collapsed, Part<Button>(window, "MinimizeButton").Visibility);
            Assert.Equal(Visibility.Collapsed, Part<Button>(window, "MaximizeButton").Visibility);

            // No resize cursor at edges that cannot be dragged.
            Assert.Equal(new Thickness(0), chrome.ResizeBorderThickness);
            Assert.Equal(window.TitleBarHeight, chrome.CaptionHeight);
        });
    }

    [Fact]
    public void AMaximizedWindowKeepsItsTitleBarOnScreen()
    {
        WpfTestHost.Run(() =>
        {
            var window = new DetailWindow(SampleDetail()) { WindowState = WindowState.Maximized };
            window.ApplyTemplate();
            var frame = Part<FrameworkElement>(window, "PART_Frame");
            var chrome = WindowChrome.GetWindowChrome(window);

            // Windows hangs a maximized window's frame off every screen edge; the content is
            // pulled back in by that much, and the drag area follows it down.
            Assert.True(frame.Margin.Top > 0);
            Assert.Equal(new Thickness(0), chrome.ResizeBorderThickness);
            Assert.Equal(frame.Margin.Top + window.TitleBarHeight, chrome.CaptionHeight);

            var maximize = Part<Button>(window, "MaximizeButton");
            Assert.Same(SystemCommands.RestoreWindowCommand, maximize.Command);
            Assert.Equal("", maximize.Content);
        });
    }
}
