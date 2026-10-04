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
        _active.HeatPeak,
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
        SKColor HeatPeak,
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

    // The dark set, kept as bright as a white card allows: each colour is the dark one where
    // that already clears 3:1 on white, otherwise the same hue stepped down only until it does.
    // Darkening every slot further turned the donut muddy. Slots 4, 13, 14 and 17 change hue
    // because stepping them down left them indistinguishable from a neighbour or another slot.
    private static readonly SKColor[] LightCategories =
    {
        new(24, 169, 77), new(74, 150, 242), new(209, 130, 64), new(220, 38, 38),
        new(42, 157, 143), new(180, 144, 51), new(155, 93, 229), new(247, 37, 133),
        new(5, 161, 199), new(181, 23, 158), new(114, 9, 183), new(58, 134, 255),
        new(202, 90, 0), new(213, 60, 217), new(255, 89, 94), new(93, 167, 1),
        new(85, 79, 254), new(11, 165, 173), new(220, 80, 100), new(147, 130, 220),
        new(42, 168, 123), new(229, 117, 82), new(101, 150, 218), new(199, 120, 204),
        new(133, 159, 18),
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
        // Hottest cells glow brighter than the accent on a dark card.
        HeatPeak: Blend(new SKColor(29, 185, 84), new SKColor(255, 255, 255), 0.15f),
        Categories: DarkCategories);

    private static readonly Palette Light = new(
        // Accent and Accent2 match AccentColor and Accent2Color in Themes/Light.xaml: the
        // brightest green and blue that still stand out from a white card (3:1).
        Accent: new SKColor(24, 169, 77),
        Accent2: new SKColor(74, 150, 242),
        Text: new SKColor(40, 45, 52),
        // Pale, so the "Other" slice recedes behind the coloured ones instead of dominating.
        Muted: new SKColor(188, 195, 202),
        Warm: new SKColor(228, 87, 46),
        Violet: new SKColor(139, 92, 246),
        Cyan: new SKColor(5, 161, 199),
        HeatEmpty: new SKColor(244, 246, 248),
        // On white the ramp runs light to dark, so the hottest cells go deeper, not paler.
        HeatPeak: new SKColor(21, 128, 61),
        Categories: LightCategories);

    private static Palette _active = Dark;

    private static SKColor Blend(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red + (b.Red - a.Red) * t),
        (byte)(a.Green + (b.Green - a.Green) * t),
        (byte)(a.Blue + (b.Blue - a.Blue) * t));
}
