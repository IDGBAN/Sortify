using System.Windows;
using System.Windows.Media;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

/// <summary>
/// The accents do two jobs in each palette - fills (checkboxes, toggles, chart bars) and small
/// text (links, ranks, trend labels) - and the light theme needs a different colour for each.
/// These pin the WCAG thresholds that split exists for, so neither half can drift.
/// </summary>
[Collection(WpfCollection.Name)]
public class PaletteContrastTests
{
    private static Color Colour(string key) => ((SolidColorBrush)Application.Current.Resources[key]).Color;

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(string a, string b)
    {
        double la = Luminance(Colour(a)), lb = Luminance(Colour(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static void AtLeast(double ratio, string foreground, string background) =>
        Assert.True(Contrast(foreground, background) >= ratio,
            $"{foreground} on {background} is {Contrast(foreground, background):0.00}:1, needs {ratio}:1 ({ThemeService.Current})");

    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    public void AccentFillsStandOutAndCarryReadableText(AppTheme theme)
    {
        WpfTestHost.Run(() =>
        {
            ThemeService.Apply(theme);

            // Controls and chart marks: 3:1 against the card they sit on.
            AtLeast(3, "AccentBrush", "PanelBrush");
            AtLeast(3, "Accent2Brush", "PanelBrush");

            // Button labels, the checked segment and the calendar's selected day sit on the fill.
            AtLeast(4.5, "OnAccentBrush", "AccentBrush");
            AtLeast(4.5, "OnAccentBrush", "AccentHoverBrush");
            AtLeast(4.5, "OnAccentBrush", "Accent2Brush");

            ThemeService.Apply(AppTheme.Dark);
        });
    }

    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    public void AccentTextIsReadableOnEverySurface(AppTheme theme)
    {
        WpfTestHost.Run(() =>
        {
            ThemeService.Apply(theme);

            foreach (var surface in new[] { "PanelBrush", "BgBrush" })
            {
                AtLeast(4.5, "AccentTextBrush", surface);
                AtLeast(4.5, "Accent2TextBrush", surface);
            }

            ThemeService.Apply(AppTheme.Dark);
        });
    }
}
