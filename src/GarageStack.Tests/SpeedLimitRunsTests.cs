using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

public class SpeedLimitRunsTests
{
    [Fact]
    public void Encode_ConsecutiveEqualLimits_BecomeOneRun()
    {
        var runs = SpeedLimitRuns.Encode([50, 50, 50, 80, 80]);

        Assert.Equal([3, 50, 2, 80], runs);
    }

    [Fact]
    public void Encode_UnknownLimits_AreKeptAsRunsOfZero()
    {
        // Where the limit is not known matters as much as where it is: it is the difference
        // between "you were under it" and "nobody knows what it was".
        var runs = SpeedLimitRuns.Encode([null, null, 30]);

        Assert.Equal([2, 0, 1, 30], runs);
    }

    [Fact]
    public void Encode_AValueAtOrBelowZero_CountsAsUnknown()
    {
        var runs = SpeedLimitRuns.Encode([0, -1, null, 50]);

        Assert.Equal([3, 0, 1, 50], runs);
    }

    [Fact]
    public void Encode_NoKnownLimitAnywhere_IsEmpty()
    {
        // A line nobody knows a single limit along says that best by saying nothing, rather than
        // by carrying a run of zeros through the cache and onto the wire.
        Assert.Empty(SpeedLimitRuns.Encode([null, null, null]));
        Assert.Empty(SpeedLimitRuns.Encode([]));
    }

    [Fact]
    public void RoundTrip_KeepsEverySegment()
    {
        List<int?> perSegment = [null, 100, 100, 100, null, null, 50, 30, 30];

        var decoded = SpeedLimitRuns.Decode(SpeedLimitRuns.Encode(perSegment));

        Assert.Equal(perSegment, decoded);
    }

    [Fact]
    public void Decode_HalfAPair_DropsIt()
    {
        // The runs may come from a cache row written by hand or by an older version; a count with
        // no limit after it says nothing about a stretch of road.
        var decoded = SpeedLimitRuns.Decode([2, 50, 3]);

        Assert.Equal([50, 50], decoded);
    }

    [Fact]
    public void Decode_NothingOrNonsense_IsEmpty()
    {
        Assert.Empty(SpeedLimitRuns.Decode(null));
        Assert.Empty(SpeedLimitRuns.Decode([]));
        Assert.Empty(SpeedLimitRuns.Decode([0, 50]));
    }
}
