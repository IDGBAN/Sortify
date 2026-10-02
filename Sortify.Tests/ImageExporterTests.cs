using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

/// <summary>
/// The render scale is the only part of chart export that can be checked without a live
/// visual tree, and it is the part that decides whether a very tall chart allocates a
/// sensible bitmap or an impossible one.
/// </summary>
public class ImageExporterTests
{
    private const double MaxPixels = 64_000_000;

    [Theory]
    [InlineData(800, 400)]
    [InlineData(1920, 1080)]
    [InlineData(1200, 3000)]
    public void OrdinaryCards_RenderAtFullScale(double width, double height)
    {
        Assert.Equal(2.0, ImageExporter.ScaleFor(width, height));
    }

    [Fact]
    public void AVeryTallChart_ScalesDownInsteadOfOverflowing()
    {
        // A paged bar chart at the 1000-bar ceiling is roughly this tall.
        double scale = ImageExporter.ScaleFor(1300, 34_070);

        Assert.True(scale < 2.0);
        Assert.True(scale > 0);
        Assert.True(1300 * scale * 34_070 * scale <= MaxPixels + 1);
    }

    [Fact]
    public void TheScaleNeverExceedsTwo()
    {
        Assert.Equal(2.0, ImageExporter.ScaleFor(1, 1));
        Assert.Equal(2.0, ImageExporter.ScaleFor(0.5, 0.5));
    }
}
