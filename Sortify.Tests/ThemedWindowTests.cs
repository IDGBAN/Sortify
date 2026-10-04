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
                Assert.Equal(Visibility.Visible, Part<Button>(window, "CloseButton").Visibility);
                Assert.Equal(window.Title, Part<TextBlock>(window, "TitleText").Text);
            }
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
            Assert.Equal(ThemedWindow.TitleBarHeight, chrome.ResizeBorderThickness.Top + chrome.CaptionHeight);
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
            Assert.Equal(ThemedWindow.TitleBarHeight, chrome.CaptionHeight);
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
            Assert.Equal(frame.Margin.Top + ThemedWindow.TitleBarHeight, chrome.CaptionHeight);

            var maximize = Part<Button>(window, "MaximizeButton");
            Assert.Same(SystemCommands.RestoreWindowCommand, maximize.Command);
            Assert.Equal("", maximize.Content);
        });
    }
}
