using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class TraceSegmenterTests
{
    // Roughly a kilometre apart in latitude; longitude is left alone so the distances are easy
    // to reason about.
    private static GeoCoordinate At(double km) => new(52.0 + km * 0.009, 6.0);

    [Fact]
    public void Split_TraceWithoutHoles_IsOneSegment()
    {
        var segments = TraceSegmenter.Split([At(0), At(1), At(2), At(3)], maxGapKm: 9);

        var segment = Assert.Single(segments);
        Assert.Equal(0, segment.Start);
        Assert.Equal(3, segment.End);
        Assert.Equal(4, segment.Count);
    }

    [Fact]
    public void Split_HoleWiderThanTheLimit_CutsTheTraceThere()
    {
        // Twenty kilometres between the second and third fix: the gateway stopped publishing
        // while the car kept driving.
        var segments = TraceSegmenter.Split([At(0), At(1), At(21), At(22)], maxGapKm: 9);

        Assert.Equal(2, segments.Count);
        Assert.Equal(new TraceSegment(0, 1), segments[0]);
        Assert.Equal(new TraceSegment(2, 3), segments[1]);
    }

    [Fact]
    public void Split_HoleAtTheEnd_LeavesTheLoneFixAsItsOwnSegment()
    {
        var segments = TraceSegmenter.Split([At(0), At(1), At(30)], maxGapKm: 9);

        Assert.Equal(2, segments.Count);
        Assert.Equal(1, segments[1].Count);
    }

    [Fact]
    public void Split_EmptyTrace_HasNoSegments()
        => Assert.Empty(TraceSegmenter.Split([], maxGapKm: 9));

    [Fact]
    public void LengthKm_SumsTheDistanceAlongTheLine()
    {
        var length = TraceSegmenter.LengthKm([At(0), At(1), At(2)]);

        Assert.Equal(2.0, length, 1);
    }

    [Fact]
    public void LengthKm_SingleVertex_IsZero()
        => Assert.Equal(0, TraceSegmenter.LengthKm([At(0)]));
}
