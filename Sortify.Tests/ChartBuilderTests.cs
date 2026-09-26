using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

public class ChartBuilderTests
{
    [Fact]
    public void ShortLabel_LeavesShortNamesAlone()
    {
        Assert.Equal("Short", ChartBuilder.ShortLabel("Short", max: 10));
    }

    [Fact]
    public void ShortLabel_TruncatesWithAnEllipsis()
    {
        Assert.Equal("abcdefghi…", ChartBuilder.ShortLabel("abcdefghijklmnop", max: 10));
    }

    [Fact]
    public void ShortLabel_NeverSplitsASurrogatePair()
    {
        // The emoji is two UTF-16 units straddling the cut; keeping only its first half
        // renders as a replacement box.
        string name = "abcdefgh\U0001F3B5xyz";

        string label = ChartBuilder.ShortLabel(name, max: 10);

        Assert.Equal("abcdefgh…", label);
        Assert.DoesNotContain(label, c => char.IsSurrogate(c));
    }
}
