using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sortify.ViewModels;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>How the Insights tab's cards fit the width they are given.</summary>
[Collection(WpfCollection.Name)]
public class InsightsLayoutTests
{
    // Both within the 1024 pixels a build agent's screen may be, which a window can't outgrow.
    [Theory]
    [InlineData(900, true, true)]
    [InlineData(1000, false, false)]
    public async Task ForgottenFavorites_StackTheirTables_InANarrowWindow(double windowWidth, bool sidebar, bool stacked)
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var window = new MainWindow
            {
                Width = windowWidth,
                Height = 800,
                Left = -10000,
                Top = 0,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowActivated = false,
                ShowInTaskbar = false,
            };
            try
            {
                var vm = (MainViewModel)window.DataContext;
                vm.SidebarVisible = sidebar;
                window.Show();

                // The tabs only show once there is a history to show.
                await vm.OpenSampleCommand.ExecuteAsync(null);
                window.Tabs.SelectedItem = window.Tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, "Insights"));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                Assert.Equal(stacked ? 2 : 0, Grid.GetRow(window.ForgottenArtistsTable));
                Assert.Equal(stacked ? 0 : 2, Grid.GetColumn(window.ForgottenArtistsTable));
                Assert.True(window.ForgottenArtistsTable.ActualWidth > (stacked ? 400 : 200));
                Assert.Equal(stacked ? 720 : 400, window.ForgottenCard.Height);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
