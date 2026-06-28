using System.Windows;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace Sortify;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Match the charts to the app's dark theme so legends, tooltips and axis
        // text render light-on-dark instead of the default light theme.
        LiveCharts.Configure(config => config.AddDarkTheme());
    }
}
