using System.Windows;
using System.Windows.Media;
using Sortify.Models;
using Sortify.Services;
using Sortify.Views;
using Xunit;

namespace Sortify.Tests;

/// <summary>
/// Loads the real XAML on a WPF thread. Unit tests over the engines can't catch a resource
/// key that was renamed or a style that no longer parses - those only fail when a window is
/// constructed, which in a normal run means at launch, in front of the user.
/// </summary>
[Collection(WpfCollection.Name)]
public class UiSmokeTests
{
    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    public void EveryWindowLoads(AppTheme theme)
    {
        WpfTestHost.Run(() =>
        {
            ThemeService.Apply(theme);

            // Constructing each window forces WPF to resolve every StaticResource its XAML
            // references and to instantiate every control template.
            Assert.NotNull(new MainWindow().Content);
            Assert.NotNull(new SettingsWindow(new AppSettings()).Content);
            Assert.NotNull(new DetailWindow(SampleDetail()).Content);
        });
    }

    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    public void EveryPaletteDefinesEveryBrush(AppTheme theme)
    {
        // Keys the styles reach for with DynamicResource. A palette missing one of these
        // renders that control invisible rather than throwing, so check them explicitly.
        string[] required =
        {
            "BgBrush", "PanelBrush", "PanelBrush2", "HoverBrush", "BorderBrush", "ScrollThumbBrush",
            "AccentBrush", "AccentHoverBrush", "Accent2Brush", "DangerBrush",
            "AccentTextBrush", "Accent2TextBrush",
            "TextBrush", "MutedBrush", "OnAccentBrush", "SelectionBrush",
        };

        WpfTestHost.Run(() =>
        {
            ThemeService.Apply(theme);

            foreach (var key in required)
                Assert.IsAssignableFrom<Brush>(Application.Current.Resources[key]);
        });
    }

    [Fact]
    public void ApplyingTheStartingThemeStillRunsTheSetup()
    {
        WpfTestHost.Run(() =>
        {
            // Regression: Apply() used to treat "already dark" as a no-op, which skipped
            // configuring LiveCharts and left its legends on the library's light default.
            ThemeService.Apply(AppTheme.Dark);
            ChartPaletteAssert.IsDark();

            // Re-applying is still harmless.
            ThemeService.Apply(AppTheme.Dark);
            ChartPaletteAssert.IsDark();
        });
    }

    [Fact]
    public void SwitchingThemeChangesTheBackground()
    {
        WpfTestHost.Run(() =>
        {
            ThemeService.Apply(AppTheme.Dark);
            var dark = ((SolidColorBrush)Application.Current.Resources["BgBrush"]).Color;

            ThemeService.Apply(AppTheme.Light);
            var light = ((SolidColorBrush)Application.Current.Resources["BgBrush"]).Color;

            Assert.NotEqual(dark, light);
            Assert.False(ThemeService.IsDark);

            ThemeService.Apply(AppTheme.Dark);
            Assert.True(ThemeService.IsDark);
        });
    }

    [Fact]
    public void EveryChartColourChangesWithTheTheme()
    {
        WpfTestHost.Run(() =>
        {
            ThemeService.Apply(AppTheme.Dark);
            var dark = ChartColours();

            ThemeService.Apply(AppTheme.Light);
            var light = ChartColours();

            // A colour left out of one palette would keep the other theme's value, which
            // reads as a stray dark bar on a white card.
            foreach (var (name, darkValue) in dark)
                Assert.True(darkValue != light[name], $"{name} is the same in both themes");

            ThemeService.Apply(AppTheme.Dark);
        });
    }

    private static Dictionary<string, SkiaSharp.SKColor> ChartColours() => new()
    {
        ["Accent"] = ChartPalette.Accent,
        ["Accent2"] = ChartPalette.Accent2,
        ["Text"] = ChartPalette.Text,
        ["Muted"] = ChartPalette.Muted,
        ["Warm"] = ChartPalette.Warm,
        ["Violet"] = ChartPalette.Violet,
        ["Cyan"] = ChartPalette.Cyan,
        ["HeatEmpty"] = ChartPalette.HeatEmpty,
        ["Categories[0]"] = ChartPalette.Categories[0],
    };

    /// <summary>The chart palette is Skia-side, so it is checked separately from the brushes.</summary>
    private static class ChartPaletteAssert
    {
        public static void IsDark()
        {
            Assert.True(ThemeService.IsDark);
            // The dark ramp starts on the near-black card colour.
            Assert.Equal(24, ChartPalette.HeatEmpty.Red);
        }
    }

    private static DetailResult SampleDetail() => new()
    {
        Scope = DetailScope.Artist,
        Title = "Test Artist",
        TotalMsPlayed = 120_000,
        PlayCount = 3,
        ActiveDays = 2,
        FirstPlayed = new DateTime(2024, 1, 1, 10, 0, 0),
        LastPlayed = new DateTime(2024, 1, 2, 11, 0, 0),
    };
}

/// <summary>
/// Serializes the UI tests. They share one Application and one static ThemeService, so
/// running them in parallel would have them fighting over the active palette.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class WpfCollection
{
    public const string Name = "WPF";
}
