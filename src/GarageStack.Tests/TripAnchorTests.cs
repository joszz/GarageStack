using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class TripAnchorTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 7, 0, 0, DateTimeKind.Utc);

    // Parked the evening before; the morning's first fix lands about 5.6 km north of it.
    private static readonly TripPoint Parked = new(T0.AddHours(-12), 51.60, 0.0, 0);
    private static readonly TripPoint First = new(T0, 51.65, 0.0, 50);
    private const double GapKm = 5.56;

    private static TripDto Trip(params TripPoint[] points) =>
        new(0, points[0].RecordedAt, points[^1].RecordedAt, 5.56, points.Length, points);

    // ── Which parked spots are worth looking at ──────────────────────────────

    [Theory]
    [InlineData(51.6509, false)] // 100 m: where the trip already starts
    [InlineData(51.6486, true)]  // 155 m
    [InlineData(51.40, true)]    // 28 km
    [InlineData(51.35, false)]   // 33 km: another story
    public void IsCandidate_OnlyBetweenTheDistanceLimits(double parkedLat, bool expected) =>
        Assert.Equal(expected, TripAnchor.IsCandidate(Parked with { Latitude = parkedLat }, First));

    [Fact]
    public void IsCandidate_NotWhenTheParkedFixIsNotEarlier() =>
        Assert.False(TripAnchor.IsCandidate(Parked with { RecordedAt = T0 }, First));

    // ── The odometer ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(10000, 10008, true)]   // a road a bit longer than the straight line
    [InlineData(10000, 10004, true)]   // 0.8x less a whole kilometre of rounding
    [InlineData(10000, 10003, false)]  // too little for the gap
    [InlineData(10000, 10000, false)]  // a ferry or a tow truck
    [InlineData(10000, 10017, true)]   // 3x plus a kilometre
    [InlineData(10000, 10018, false)]  // went somewhere else first
    public void OdometerAgrees_WithinTheRatios(double before, double after, bool expected) =>
        Assert.Equal(expected, TripAnchor.OdometerAgrees(GapKm, before, after));

    [Fact]
    public void OdometerAgrees_NotWithoutBothReadings()
    {
        Assert.False(TripAnchor.OdometerAgrees(GapKm, null, 10006));
        Assert.False(TripAnchor.OdometerAgrees(GapKm, 10000, null));
    }

    [Fact]
    public void OdometerAgrees_ANearbySpotWithoutAWholeKilometre()
    {
        // 400 m driven between two readings of the same whole kilometre.
        Assert.True(TripAnchor.OdometerAgrees(0.4, 10000, 10000));
    }

    // ── When the car woke up ─────────────────────────────────────────────────

    [Fact]
    public void WakeUp_IsTheFirstReportAfterTheQuietPeriod()
    {
        DateTime[] reports = [T0.AddMinutes(-2), T0.AddMinutes(-9), T0.AddHours(-11.8)];

        Assert.Equal(T0.AddMinutes(-9), TripAnchor.WakeUp(Parked, First, reports, complete: true));
    }

    [Fact]
    public void WakeUp_IsNullWhenTheFirstFixEndedTheQuiet()
    {
        Assert.Null(TripAnchor.WakeUp(Parked, First, [], complete: true));
        Assert.Null(TripAnchor.WakeUp(Parked, First, [T0.AddHours(-11.8)], complete: true));
    }

    [Fact]
    public void WakeUp_IsTheOldestReportWhenTheQuietFollowsTheParkedFix()
    {
        DateTime[] reports = [T0.AddMinutes(-2), T0.AddMinutes(-9)];

        Assert.Equal(T0.AddMinutes(-9), TripAnchor.WakeUp(Parked, First, reports, complete: true));
    }

    [Fact]
    public void WakeUp_IsNullWhenTheCarNeverWentQuiet()
    {
        var parked = Parked with { RecordedAt = T0.AddMinutes(-40) };
        DateTime[] reports = [T0.AddMinutes(-10), T0.AddMinutes(-25)];

        Assert.Null(TripAnchor.WakeUp(parked, First, reports, complete: true));
    }

    [Fact]
    public void WakeUp_IsNullWhenTheReportsReadDoNotReachTheQuiet()
    {
        DateTime[] reports = [T0.AddMinutes(-2), T0.AddMinutes(-9)];

        Assert.Null(TripAnchor.WakeUp(Parked, First, reports, complete: false));
    }

    // ── When the car left ────────────────────────────────────────────────────

    [Fact]
    public void Departure_IsTheWakeUpWhenTheGapFitsInIt()
    {
        Assert.Equal(T0.AddMinutes(-9), TripAnchor.Departure(Parked, First, T0.AddMinutes(-9)));
    }

    [Fact]
    public void Departure_WithoutAWakeUp_IsTheGapAtTownSpeedBeforeTheFirstFix()
    {
        var departure = TripAnchor.Departure(Parked, First, null);

        // 5.56 km at 25 km/h is a little over 13 minutes.
        Assert.InRange(departure, T0.AddMinutes(-13.5), T0.AddMinutes(-13.2));
    }

    [Fact]
    public void Departure_IgnoresAWakeUpReportedMidDrive()
    {
        // 5.56 km in one minute: the car was already well on its way.
        var departure = TripAnchor.Departure(Parked, First, T0.AddMinutes(-1));

        Assert.Equal(TripAnchor.Departure(Parked, First, null), departure);
    }

    [Fact]
    public void Departure_IsNeverBeforeTheParkedFix()
    {
        var parked = Parked with { RecordedAt = T0.AddMinutes(-5) };

        Assert.Equal(parked.RecordedAt, TripAnchor.Departure(parked, First, null));
    }

    // ── Starting the trip there ──────────────────────────────────────────────

    [Fact]
    public void Apply_StartsTheTripAtTheParkedSpot()
    {
        var trip = Trip(First, First with { RecordedAt = T0.AddMinutes(5), Latitude = 51.70 });
        var departure = T0.AddMinutes(-10);

        var anchored = TripAnchor.Apply(trip, Parked, departure, 10000, 10006);

        Assert.NotNull(anchored);
        Assert.Equal(new TripPoint(departure, 51.60, 0.0, 0, TripPointSource.Anchored), anchored.Points[0]);
        Assert.Equal(trip.Points, anchored.Points.Skip(1));
        Assert.Equal(departure, anchored.StartedAt);
        Assert.Equal(3, anchored.PointCount);
        Assert.Equal(11.12, anchored.DistanceKm);
    }

    [Fact]
    public void Apply_NotWhenTheOdometerDisagrees()
    {
        var trip = Trip(First, First with { RecordedAt = T0.AddMinutes(5), Latitude = 51.70 });

        Assert.Null(TripAnchor.Apply(trip, Parked, T0.AddMinutes(-10), 10000, 10000));
    }

    [Fact]
    public void Apply_NotWhenLeavingThenMeansAnImpossibleSpeed()
    {
        // Parked a minute before a first fix 5.6 km away: only a glitch covers that.
        var parked = Parked with { RecordedAt = T0.AddMinutes(-1) };
        var trip = Trip(First, First with { RecordedAt = T0.AddMinutes(5), Latitude = 51.70 });

        Assert.Null(TripAnchor.Apply(trip, parked, TripAnchor.Departure(parked, First, null), 10000, 10006));
    }
}
