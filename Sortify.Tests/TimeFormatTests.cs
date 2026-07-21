using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

public class TimeFormatTests
{
    [Fact]
    public void HhMmSs_HoursCanExceed24()
    {
        var span = new TimeSpan(2, 3, 4, 5); // 2 days, 3 hours -> 51 hours
        Assert.Equal("51:04:05", TimeFormat.HhMmSs(span));
    }

    [Fact]
    public void HhMmSs_PadsSmallValues()
    {
        Assert.Equal("00:05:09", TimeFormat.HhMmSs(new TimeSpan(0, 5, 9)));
    }

    [Theory]
    [InlineData(2, 4, 30, 0, "2d 4h 30m")]
    [InlineData(0, 3, 15, 0, "3h 15m")]
    [InlineData(0, 0, 12, 40, "12m 40s")]
    public void Friendly_PicksTheRightGranularity(int d, int h, int m, int s, string expected)
    {
        Assert.Equal(expected, TimeFormat.Friendly(new TimeSpan(d, h, m, s)));
    }

    [Fact]
    public void Timestamp_FormatsAndHandlesNull()
    {
        Assert.Equal("2023-05-10 14:30", TimeFormat.Timestamp(new DateTime(2023, 5, 10, 14, 30, 0)));
        Assert.Equal("2023-05-10 14:30", TimeFormat.Timestamp((DateTime?)new DateTime(2023, 5, 10, 14, 30, 0)));
        Assert.Equal(string.Empty, TimeFormat.Timestamp((DateTime?)null));
    }
}
