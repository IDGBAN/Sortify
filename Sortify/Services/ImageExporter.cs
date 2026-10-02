using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sortify.Services;

/// <summary>
/// Renders a piece of the live UI - a chart card - to a PNG, so a chart can be dropped
/// into a document without a screenshot tool.
/// </summary>
public static class ImageExporter
{
    /// <summary>Rendered at 2x so the result stays sharp when scaled or printed.</summary>
    private const double Scale = 2.0;

    private const double BaseDpi = 96.0;

    /// <summary>
    /// Ceiling on the pixels of a single export. A RenderTargetBitmap allocates four bytes
    /// per pixel up front, so an unbounded scale-up of a tall paged bar chart is an
    /// out-of-memory crash rather than a slow save; 64 MP is ~256 MB and well past any
    /// chart worth looking at.
    /// </summary>
    private const double MaxPixels = 64_000_000;

    /// <summary>
    /// Renders <paramref name="element"/> onto <paramref name="background"/> and returns the
    /// bitmap, or null when the element has no size yet (never laid out, or collapsed).
    /// <paramref name="scale"/> defaults to 2x; something already drawn at its final pixel
    /// size, like the year-in-review card, passes 1.
    /// </summary>
    public static BitmapSource? Render(FrameworkElement element, Brush? background, double scale = Scale)
    {
        double width = element.ActualWidth;
        double height = element.ActualHeight;
        if (width <= 0 || height <= 0 || !double.IsFinite(width) || !double.IsFinite(height))
            return null;

        // A VisualBrush of the live element captures whatever it currently shows, including
        // the SkiaSharp surface the charts draw into.
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, width, height);
            if (background is not null)
                context.DrawRectangle(background, null, bounds);
            context.DrawRectangle(new VisualBrush(element), null, bounds);
        }

        double factor = ScaleFor(width, height, scale);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * factor),
            (int)Math.Ceiling(height * factor),
            BaseDpi * factor, BaseDpi * factor,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// <see cref="Scale"/>, backed off far enough to keep the bitmap under
    /// <see cref="MaxPixels"/>. An element large enough to need it downsamples rather than
    /// failing, which beats no image at all.
    /// </summary>
    internal static double ScaleFor(double width, double height, double scale = Scale)
    {
        double pixels = width * scale * height * scale;
        if (pixels <= MaxPixels)
            return scale;

        // Both dimensions shrink, so the area shrinks by the square of the factor.
        return scale * Math.Sqrt(MaxPixels / pixels);
    }

    /// <summary>Renders and writes a PNG. Returns false when there was nothing to render.</summary>
    public static bool SavePng(FrameworkElement element, Brush? background, string path, double scale = Scale)
    {
        var bitmap = Render(element, background, scale);
        if (bitmap is null)
            return false;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
        return true;
    }

    /// <summary>Renders onto the clipboard. Returns false when there was nothing to render.</summary>
    public static bool CopyToClipboard(FrameworkElement element, Brush? background, double scale = Scale)
    {
        var bitmap = Render(element, background, scale);
        if (bitmap is null)
            return false;

        Clipboard.SetImage(bitmap);
        return true;
    }
}
