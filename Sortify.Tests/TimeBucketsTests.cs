using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

public class TimeBucketsTests
{
    [Fact]
    public void Between_ListsEveryBucket_EvenTheEmptyOnes()
    {
        var weeks = TimeBuckets.Between(new DateTime(2024, 3, 13), new DateTime(2024, 4, 2), ChartBuilder.TimeGranularity.Weekly, DayOfWeek.Monday);

        Assert.Equal(
            new[] { new DateTime(2024, 3, 11), new DateTime(2024, 3, 18), new DateTime(2024, 3, 25), new DateTime(2024, 4, 1) },
            weeks);
    }

    [Fact]
    public void Between_OneDay_IsOneBucket()
    {
        var day = new DateTime(2024, 3, 13, 21, 0, 0);

        Assert.Equal(new[] { day.Date }, TimeBuckets.Between(day, day, ChartBuilder.TimeGranularity.Daily));
    }

    [Theory]
    [InlineData(ChartBuilder.TimeGranularity.Daily)]
    [InlineData(ChartBuilder.TimeGranularity.Weekly)]
    [InlineData(ChartBuilder.TimeGranularity.Monthly)]
    public void Between_StopsAtTheEndOfTheCalendar(ChartBuilder.TimeGranularity granularity)
    {
        var buckets = TimeBuckets.Between(new DateTime(9999, 11, 20), DateTime.MaxValue, granularity);

        Assert.Equal(TimeBuckets.Of(DateTime.MaxValue, granularity), buckets[^1]);
    }
}
