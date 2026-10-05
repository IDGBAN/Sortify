using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;

namespace Sortify.Tests;

/// <summary>The tab strip, styled in Themes/Controls.xaml, when the window is too narrow for one row.</summary>
[Collection(WpfCollection.Name)]
public class TabStripTests
{
    [Fact]
    public async Task TabsThatWrap_StayWhereTheyAre_AndArrowKeysStillMoveBetweenThem()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var tabs = new TabControl();
            foreach (var name in new[] { "Overview", "Tracks", "Artists", "Albums", "Years", "Podcasts" })
                tabs.Items.Add(new TabItem { Header = name, Content = name });
            var window = new Window
            {
                Content = tabs,
                Width = 300,
                Height = 300,
                Left = -10000,
                Top = 0,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowActivated = false,
                ShowInTaskbar = false,
            };
            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var first = (TabItem)tabs.Items[0];
                var last = (TabItem)tabs.Items[^1];
                Point Where(TabItem item) => item.TranslatePoint(new Point(0, 0), tabs);
                var before = Where(first);
                Assert.True(Where(last).Y > before.Y, "the tabs should need a second row at this width");

                tabs.SelectedItem = last;
                tabs.UpdateLayout();
                Assert.Equal(before, Where(first));

                first.Focus();
                first.MoveFocus(new TraversalRequest(FocusNavigationDirection.Right));
                Assert.Same(tabs.Items[1], Keyboard.FocusedElement);
                Assert.True(((TabItem)tabs.Items[1]).IsSelected);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
