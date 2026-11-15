using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class UnseenTripTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private static TripPoint Spot(TimeSpan after, double lat) => new(T0 + after, lat, 0.0, 0, TripPointSource.Anchored);

    // Parked at 51.60 at T0, seen parked 22.24 km north two hours later.
    private static readonly UnseenDrive North = new([Spot(TimeSpan.Zero, 51.60), Spot(TimeSpan.FromHours(2), 51.80)]);

    [Theory]
    [InlineData(22.24, 24000.0, 24025.0, true)]
    [InlineData(22.24, 24000.0, 24045.0, true)]   // roads winding up to twice the straight line
    [InlineData(22.24, 24000.0, 24000.9, false)]  // a tow truck or a ferry
    [InlineData(22.24, 24000.0, 24015.0, false)]  // too little for the straight line
    [InlineData(22.24, 24000.0, 24047.0, false)]  // went somewhere else first
    [InlineData(1.6, 24000.0, 24001.0, true)]
    [InlineData(1.6, 24000.0, 24000.5, false)]    // under the whole kilometre the odometer counts
    [InlineData(22.24, null, 24025.0, false)]
    [InlineData(22.24, 24000.0, null, false)]
    public void OdometerConfirms_ADriveAboutAsFarAsTheStraightLine(double straightKm, double? before, double? after, bool confirms) =>
        Assert.Equal(confirms, UnseenTrip.OdometerConfirms(straightKm, before, after));

    [Fact]
    public void Build_WithoutAWakeUp_LeavesTheOdometersDistanceAtFiftyBeforeItWasSeen()
    {
        var trip = UnseenTrip.Build(3, North, wokeAt: null, 24000, 24025)!;

        Assert.Equal((3, T0.AddHours(1.5), T0.AddHours(2), 25.0, 2), (trip.Index, trip.StartedAt, trip.EndedAt, trip.DistanceKm, trip.PointCount));
        Assert.Equal(trip.StartedAt, trip.Points[0].RecordedAt);
        Assert.Equal(North.Points[1] with { Speed = null }, trip.Points[1]);
        Assert.Null(trip.MaxSpeedKmh);
    }

    [Fact]
    public void Build_LeavesWhenTheCarWokeUp()
    {
        var trip = UnseenTrip.Build(0, North, wokeAt: T0.AddHours(1), 24000, 24025)!;

        Assert.Equal(T0.AddHours(1), trip.StartedAt);
    }

    // A wake-up a minute before it was seen 22 km off was reported mid-drive.
    [Fact]
    public void Build_IgnoresAWakeUpTooLateToHaveDrivenThere()
    {
        var trip = UnseenTrip.Build(0, North, wokeAt: T0.AddHours(2).AddMinutes(-1), 24000, 24025)!;

        Assert.Equal(T0.AddHours(1.5), trip.StartedAt);
    }

    [Fact]
    public void Build_NeverLeavesBeforeTheCarStoodThere()
    {
        var soon = new UnseenDrive([Spot(TimeSpan.Zero, 51.60), Spot(TimeSpan.FromMinutes(20), 51.80)]);

        Assert.Equal(T0, UnseenTrip.Build(0, soon, wokeAt: null, 24000, 24025)!.StartedAt);
    }

    [Theory]
    [InlineData(24000.0, 24000.0)]
    [InlineData(24000.0, 24080.0)]
    [InlineData(null, 24025.0)]
    public void Build_RefusesWhenTheOdometerDoesNotConfirm(double? before, double? after) =>
        Assert.Null(UnseenTrip.Build(0, North, wokeAt: null, before, after));

    // 22 km in five minutes is no drive.
    [Fact]
    public void Build_RefusesASpeedNoCarDrives()
    {
        var jump = new UnseenDrive([Spot(TimeSpan.Zero, 51.60), Spot(TimeSpan.FromMinutes(5), 51.80)]);

        Assert.Null(UnseenTrip.Build(0, jump, wokeAt: null, 24000, 24025));
    }

    // The moving fix a poll caught on the way keeps the speed it reported.
    [Fact]
    public void Build_KeepsTheSpeedOfAFixCaughtOnTheWay()
    {
        var caught = new TripPoint(T0.AddHours(1.5), 51.75, 0.0, 80);
        var drive = new UnseenDrive([North.Points[0], caught, North.Points[1]]);

        var trip = UnseenTrip.Build(0, drive, wokeAt: null, 24000, 24026)!;

        Assert.Equal(80, trip.MaxSpeedKmh);
    }

    [Fact]
    public void IsCandidate_NeedsAQuietSpellNoLongerThanASleepingCarsAndMoreThanDrift()
    {
        var stood = North.Points[0];

        Assert.True(UnseenTrip.IsCandidate(stood, North.Points[1]));
        Assert.False(UnseenTrip.IsCandidate(stood, stood with { RecordedAt = T0.AddMinutes(1), Latitude = 51.70 }));
        Assert.False(UnseenTrip.IsCandidate(stood, stood with { RecordedAt = T0.AddDays(3), Latitude = 51.70 }));
        Assert.False(UnseenTrip.IsCandidate(stood, stood with { RecordedAt = T0.AddHours(2), Latitude = 51.61 }));
    }
}
