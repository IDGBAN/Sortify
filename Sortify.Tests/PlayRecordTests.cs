using Sortify.Models;
using Sortify.Services;
using Xunit;

namespace Sortify.Tests;

public class PlayRecordTests
{
    [Fact]
    public void TheDateSpan_RunsFromTheFirstPlayToTheLast_LeavingOutUndatedOnes()
    {
        var plays = new[]
        {
            new PlayRecord { Timestamp = new DateTime(2024, 3, 1) },
            new PlayRecord { Timestamp = DateTime.MinValue },
            new PlayRecord { Timestamp = new DateTime(2023, 7, 9) },
        };

        Assert.Equal((new DateTime(2023, 7, 9), new DateTime(2024, 3, 1)), PlayRecord.DateSpan(plays));
        Assert.Equal(((DateTime?)null, (DateTime?)null), PlayRecord.DateSpan(new[] { new PlayRecord() }));
    }

    [Fact]
    public void APlayWithNoPlatform_HasNoDevice()
    {
        Assert.Equal(string.Empty, Devices.FamilyOf(string.Empty));
        Assert.Equal("Mobile", Devices.FamilyOf("iOS 16.1 (iPhone14,5)"));
    }
}
