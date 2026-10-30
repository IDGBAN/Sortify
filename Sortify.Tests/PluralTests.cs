using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

public class PluralTests
{
    [Theory]
    [InlineData(0, "0 plays")]
    [InlineData(1, "1 play")]
    [InlineData(2048, "2,048 plays")]
    public void ACount_TakesTheWordThatFitsIt(long count, string expected)
    {
        using var _ = CultureScope.Named("en-US");
        Assert.Equal(expected, Plural.Of(count, "play"));
    }

    [Fact]
    public void AnIrregularPlural_IsUsedAsGiven()
    {
        Assert.Equal("1 country", Plural.Of(1, "country", "countries"));
        Assert.Equal("3 countries", Plural.Of(3, "country", "countries"));
    }
}
