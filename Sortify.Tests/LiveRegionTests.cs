using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>Messages marked as live regions reach a screen reader when they change.</summary>
[Collection(WpfCollection.Name)]
public class LiveRegionTests
{
    private static Window Host(UIElement content) => new()
    {
        Content = content,
        Left = -10000,
        Top = 0,
        Width = 300,
        Height = 200,
        WindowStartupLocation = WindowStartupLocation.Manual,
        ShowActivated = false,
        ShowInTaskbar = false,
    };

    private static async Task Settle()
    {
        for (int i = 0; i < 3; i++)
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    [Fact]
    public async Task AMessage_IsReadOutWhenItChangesOrAppears_ButNotWhenItsWindowOpens()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var block = new TextBlock { Text = "Ready" };
            var heard = new List<string>();
            void Listen(UIElement announced)
            {
                if (ReferenceEquals(announced, block))
                    heard.Add(block.Text);
            }

            LiveRegion.SetAnnounce(block, AutomationLiveSetting.Polite);
            LiveRegion.Announcing += Listen;
            var window = Host(block);
            try
            {
                window.Show();
                await Settle();
                Assert.Empty(heard);
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(block));

                block.Text = "Saved";
                await Settle();
                Assert.Equal(new[] { "Saved" }, heard);

                // Hidden, its text changes unheard; shown again with new text, that's one message.
                block.Visibility = Visibility.Collapsed;
                block.Text = "Not shown";
                await Settle();
                block.Text = "Copied";
                block.Visibility = Visibility.Visible;
                await Settle();
                Assert.Equal(new[] { "Saved", "Copied" }, heard);

                LiveRegion.SetAnnounce(block, AutomationLiveSetting.Off);
                block.Text = "Quiet";
                await Settle();
                Assert.Equal(2, heard.Count);
                Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(block));
            }
            finally
            {
                LiveRegion.Announcing -= Listen;
                window.Close();
            }
        });
    }

    [Fact]
    public void TheBreakdownsCopyButton_SaysWhetherTheCopyWorked()
    {
        WpfTestHost.Run(() =>
        {
            var window = new DetailWindow(new Sortify.Models.DetailResult { Scope = Sortify.Models.DetailScope.Artist, Title = "Band" });
            UIElement? heard = null;
            void Listen(UIElement announced) => heard = announced;

            LiveRegion.Announcing += Listen;
            try
            {
                LiveRegion.Announce(window.CopyButton);
            }
            finally
            {
                LiveRegion.Announcing -= Listen;
            }

            Assert.Same(window.CopyButton, heard);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(window.CopyButton));
        });
    }

    [Fact]
    public void EveryLiveRegionInTheMainWindow_IsReadOut()
    {
        WpfTestHost.Run(() =>
        {
            var regions = Descendants(new MainWindow())
                .Where(d => AutomationProperties.GetLiveSetting(d) != AutomationLiveSetting.Off)
                .ToList();

            Assert.True(regions.Count >= 5);
            Assert.All(regions, d => Assert.Equal(AutomationProperties.GetLiveSetting(d), LiveRegion.GetAnnounce(d)));
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
