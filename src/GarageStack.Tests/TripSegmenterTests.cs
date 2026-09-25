using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class TripSegmenterTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private static TripPoint Fix(double minutes, double lat, double? speed) =>
        new(T0.AddMinutes(minutes), lat, 0.0, speed);

    // Far enough past every fix that each segment has ended.
    private static readonly DateTime LongAfter = T0.AddDays(1);

    // ── Where one trip ends and the next begins ──────────────────────────────

    [Fact]
    public void BackToBackWithParkStop_ReturnsTwoTrips()
    {
        TripPoint[] fixes =
        [
            // Trip 1: A -> B
            Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(10, 51.60, 50),
            // Parked at B for 10 minutes (> 5 min threshold)
            Fix(11, 51.60, 0), Fix(15, 51.60, 0), Fix(20, 51.60, 0),
            // Trip 2: B -> A
            Fix(21, 51.60, 50), Fix(25, 51.55, 50), Fix(30, 51.50, 50),
        ];

        Assert.Equal(2, TripSegmenter.Segment(fixes, LongAfter).Trips.Count);
    }

    [Fact]
    public void BriefTrafficStop_DoesNotSplitTrip()
    {
        TripPoint[] fixes =
        [
            Fix(0, 51.50, 50), Fix(5, 51.55, 50),
            // Brief stop at a traffic light (2 minutes < 5 min threshold)
            Fix(6, 51.55, 0), Fix(7, 51.55, 0),
            Fix(8, 51.55, 50), Fix(13, 51.60, 50),
        ];

        Assert.Single(TripSegmenter.Segment(fixes, LongAfter).Trips);
    }

    [Fact]
    public void NullSpeedParkingGap_SplitsIntoTwoTrips()
    {
        // GPS-only fixes (Speed = null) between two trips must still trigger the 5-minute parking split.
        TripPoint[] fixes =
        [
            Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(10, 51.60, 50),
            // Gateway sends GPS-only updates (no speed topic) while parked.
            Fix(11, 51.60, null), Fix(15, 51.60, null), Fix(20, 51.60, null),
            // Second trip starts after >5 min of null-speed fixes.
            Fix(21, 51.60, 50), Fix(25, 51.55, 50), Fix(30, 51.50, 50),
        ];

        Assert.Equal(2, TripSegmenter.Segment(fixes, LongAfter).Trips.Count);
    }

    [Fact]
    public void BriefNullSpeedBlip_DoesNotSplitTrip()
    {
        // A single null-speed GPS update mid-trip (< 5 min) must not split the trip.
        TripPoint[] fixes =
        [
            Fix(0, 51.50, 50), Fix(5, 51.55, 50),
            Fix(6, 51.55, null), // brief null-speed blip
            Fix(7, 51.56, 50), Fix(12, 51.60, 50),
        ];

        Assert.Single(TripSegmenter.Segment(fixes, LongAfter).Trips);
    }

    [Fact]
    public void HalfAnHourWithoutFixes_SplitsIntoTwoTrips()
    {
        TripPoint[] fixes = [Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(36, 51.56, 50), Fix(40, 51.60, 50)];

        Assert.Equal(2, TripSegmenter.Segment(fixes, LongAfter).Trips.Count);
    }

    [Fact]
    public void ImplausibleJump_DiscardsTheTrip()
    {
        // 55 km in one minute is a positioning glitch, not a drive.
        TripPoint[] fixes = [Fix(0, 51.50, 50), Fix(1, 52.00, 50), Fix(2, 52.01, 50)];

        Assert.Empty(TripSegmenter.Segment(fixes, LongAfter).Trips);
    }

    [Fact]
    public void DiscardedSegment_LeavesNoGapInTheIndexes()
    {
        TripPoint[] fixes =
        [
            Fix(0, 51.50, 50), Fix(5, 51.55, 50),
            Fix(6, 51.55, 0),
            // A 20 m shuffle across the car park: under the 0.1 km a trip needs.
            Fix(12, 51.5500, 5), Fix(13, 51.5502, 5),
            Fix(14, 51.5502, 0),
            Fix(20, 51.56, 50), Fix(25, 51.60, 50),
        ];

        var trips = TripSegmenter.Segment(fixes, LongAfter).Trips;

        Assert.Equal([0, 1], trips.Select(t => t.Index));
    }

    // ── Whether the last segment has ended ───────────────────────────────────

    [Fact]
    public void StillMovingAtTheHorizon_IsOpen()
    {
        TripPoint[] fixes = [Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(10, 51.60, 50)];

        var result = TripSegmenter.Segment(fixes, T0.AddMinutes(11));

        Assert.Single(result.Trips);
        Assert.Equal(T0, result.OpenSince);
        Assert.Empty(result.Closed);
    }

    [Fact]
    public void ParkedForFiveMinutesByTheHorizon_IsClosed()
    {
        TripPoint[] fixes = [Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(6, 51.55, 0)];

        var result = TripSegmenter.Segment(fixes, T0.AddMinutes(11));

        Assert.Null(result.OpenSince);
        Assert.Single(result.Closed);
    }

    [Fact]
    public void ParkedForLessThanFiveMinutesByTheHorizon_IsOpen()
    {
        // The car may drive on within the five minutes, which keeps it the same trip.
        TripPoint[] fixes = [Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(6, 51.55, 0)];

        var result = TripSegmenter.Segment(fixes, T0.AddMinutes(10));

        Assert.Equal(T0, result.OpenSince);
    }

    [Fact]
    public void QuietForMoreThanHalfAnHourByTheHorizon_IsClosed()
    {
        TripPoint[] fixes = [Fix(0, 51.50, 50), Fix(5, 51.55, 50)];

        Assert.Equal(T0, TripSegmenter.Segment(fixes, T0.AddMinutes(35)).OpenSince);
        Assert.Null(TripSegmenter.Segment(fixes, T0.AddMinutes(35.1)).OpenSince);
    }

    [Fact]
    public void Closed_LeavesOutOnlyTheOpenTrip()
    {
        TripPoint[] fixes =
        [
            Fix(0, 51.50, 50), Fix(5, 51.55, 50), Fix(6, 51.55, 0),
            Fix(20, 51.56, 50), Fix(25, 51.60, 50),
        ];

        var result = TripSegmenter.Segment(fixes, T0.AddMinutes(26));

        Assert.Equal(2, result.Trips.Count);
        Assert.Equal(T0.AddMinutes(20), result.OpenSince);
        Assert.Equal(T0, Assert.Single(result.Closed).StartedAt);
    }

    [Fact]
    public void NoFixes_NoTripsAndNothingOpen()
    {
        var result = TripSegmenter.Segment([], T0);

        Assert.Empty(result.Trips);
        Assert.Null(result.OpenSince);
    }
}
