using SkiaSharp;

namespace Sortify.Services;

/// <summary>
/// The colours <see cref="ChartBuilder"/> paints with. Skia has no notion of WPF resources,
/// so the values are mirrored here and refreshed by <see cref="ThemeService"/> whenever the
/// palette changes; charts are rebuilt afterwards.
/// </summary>
public static class ChartPalette
{
    /// <summary>Primary accent: listening-time series.</summary>
    public static SKColor Accent => _active.Accent;

    /// <summary>Secondary accent: play-count series.</summary>
    public static SKColor Accent2 => _active.Accent2;

    /// <summary>Axis labels and data labels.</summary>
    public static SKColor Text => _active.Text;

    /// <summary>Fill for the "Other" slice of a donut.</summary>
    public static SKColor Muted => _active.Muted;

    /// <summary>Skip / warning series.</summary>
    public static SKColor Warm => _active.Warm;

    /// <summary>Podcast show bars.</summary>
    public static SKColor Violet => _active.Violet;

    /// <summary>Podcast episode bars.</summary>
    public static SKColor Cyan => _active.Cyan;

    /// <summary>The coldest heatmap stop, chosen to blend into the card behind it.</summary>
    public static SKColor HeatEmpty => _active.HeatEmpty;

    /// <summary>Categorical colours for donut slices, in order.</summary>
    public static SKColor[] Categories => _active.Categories;

    /// <summary>Switches every colour to the requested palette. Called by ThemeService.</summary>
    public static void Apply(bool dark) => _active = dark ? Dark : Light;

    /// <summary>The heatmap ramp, coldest first.</summary>
    public static SKColor[] HeatRamp => new[]
    {
        HeatEmpty,
        Blend(HeatEmpty, Accent, 0.35f),
        Accent,
        Blend(Accent, new SKColor(255, 255, 255), 0.15f),
    };

    /// <summary>
    /// One theme's colours. Declaring them as a set means a colour added here has to be
    /// given a value in both themes rather than silently keeping the other one's.
    /// </summary>
    private sealed record Palette(
        SKColor Accent,
        SKColor Accent2,
        SKColor Text,
        SKColor Muted,
        SKColor Warm,
        SKColor Violet,
        SKColor Cyan,
        SKColor HeatEmpty,
        SKColor[] Categories);

    private static readonly SKColor[] DarkCategories =
    {
        new(29, 185, 84), new(80, 156, 248), new(244, 162, 97), new(231, 111, 81),
        new(42, 157, 143), new(233, 196, 106), new(155, 93, 229), new(247, 37, 133),
        new(76, 201, 240), new(181, 23, 158), new(114, 9, 183), new(58, 134, 255),
        new(255, 159, 28), new(46, 196, 182), new(255, 89, 94), new(124, 200, 60),
        new(255, 196, 61), new(0, 187, 196), new(220, 80, 100), new(147, 130, 220),
        new(72, 191, 145), new(255, 140, 105), new(120, 170, 240), new(210, 130, 215),
        new(176, 205, 80),
    };

    // Same hues, darkened so they hold their own against a white card.
    private static readonly SKColor[] LightCategories =
    {
        new(21, 128, 61), new(47, 111, 208), new(202, 118, 42), new(198, 76, 47),
        new(28, 122, 112), new(179, 138, 44), new(120, 63, 191), new(198, 24, 104),
        new(23, 149, 189), new(147, 17, 128), new(90, 8, 145), new(37, 99, 205),
        new(203, 118, 12), new(24, 150, 139), new(206, 55, 60), new(85, 152, 34),
        new(191, 140, 20), new(0, 137, 145), new(178, 51, 71), new(105, 90, 186),
        new(38, 145, 105), new(203, 98, 68), new(72, 122, 197), new(160, 88, 166),
        new(126, 150, 40),
    };

    private static readonly Palette Dark = new(
        Accent: new SKColor(29, 185, 84),
        Accent2: new SKColor(80, 156, 248),
        Text: new SKColor(220, 220, 220),
        Muted: new SKColor(110, 110, 110),
        Warm: new SKColor(231, 111, 81),
        Violet: new SKColor(155, 93, 229),
        Cyan: new SKColor(76, 201, 240),
        HeatEmpty: new SKColor(24, 24, 24),
        Categories: DarkCategories);

    private static readonly Palette Light = new(
        // Matches AccentColor in Themes/Light.xaml.
        Accent: new SKColor(21, 128, 61),
        Accent2: new SKColor(47, 111, 208),
        Text: new SKColor(40, 45, 52),
        Muted: new SKColor(150, 157, 165),
        Warm: new SKColor(198, 76, 47),
        Violet: new SKColor(120, 63, 191),
        Cyan: new SKColor(23, 149, 189),
        HeatEmpty: new SKColor(244, 246, 248),
        Categories: LightCategories);

    private static Palette _active = Dark;

    private static SKColor Blend(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red + (b.Red - a.Red) * t),
        (byte)(a.Green + (b.Green - a.Green) * t),
        (byte)(a.Blue + (b.Blue - a.Blue) * t));
}
