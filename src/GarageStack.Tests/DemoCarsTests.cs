using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Demo;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

/// <summary>
/// The two demo cars tell stories of their own, so switching between them shows a different car:
/// where they are, what they did and what a visitor changes stay apart.
/// </summary>
public class DemoCarsTests
{
    private static readonly Vehicle Zs = DemoVehicleRepository.DemoVehicle;
    private static readonly Vehicle Hs = DemoVehicleRepository.SecondDemoVehicle;

    private static readonly DateTime To = DateTime.UtcNow.AddDays(1);
    private static readonly DateTime From = To.AddDays(-366);

    [Fact]
    public async Task TheZsIsOnTheRoad_AndTheHsParkedAtHomeOnItsCharger()
    {
        var ct = TestContext.Current.CancellationToken;
        var telemetry = new DemoTelemetryRepository();

        var zs = (await telemetry.GetMergedLatestAsync(Zs.Id, ct))!;
        var hs = (await telemetry.GetMergedLatestAsync(Hs.Id, ct))!;

        Assert.True(zs.Speed > 0);
        Assert.True(zs.CurrentJourneyDistance > 0);
        Assert.Equal((0.0, 0.0), (hs.Speed, hs.CurrentJourneyDistance));
        Assert.Equal((false, true), (hs.EngineRunning, hs.ChargerConnected));

        Assert.NotEqual(zs.OdometerKm, hs.OdometerKm);
        Assert.NotEqual(zs.HvTotalCapacityKwh, hs.HvTotalCapacityKwh);
        Assert.True(GeoHelper.Haversine(zs.Latitude!.Value, zs.Longitude!.Value, hs.Latitude!.Value, hs.Longitude!.Value) > 20);
        Assert.Equal((Zs.Id, Hs.Id), (zs.VehicleId, hs.VehicleId));

        // The ZS takes a charge target and the HS does not, as their configuration says.
        Assert.Equal(80, zs.SocTargetPercent);
        Assert.Null(hs.SocTargetPercent);
    }

    [Fact]
    public async Task AnOverride_ChangesTheCarItIsFor_AndLeavesTheOtherAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        var telemetry = new DemoTelemetryRepository();
        var zsBefore = (await telemetry.GetMergedLatestAsync(Zs.Id, ct))!;

        telemetry.ApplyOverride(Hs.Id, new DemoStatusOverrideDto(
            IsLocked: false, null, null, DriverDoorOpen: true, null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, EvSocPercent: 35, null, null, null));

        var hs = (await telemetry.GetMergedLatestAsync(Hs.Id, ct))!;
        var zs = (await telemetry.GetMergedLatestAsync(Zs.Id, ct))!;
        Assert.Equal((false, true, 35.0), (hs.IsLocked, hs.DriverDoorOpen, hs.EvSocPercent));
        // Its range and stored energy follow from its own, smaller battery.
        Assert.Equal(35, hs.ElectricRangeKm);
        Assert.Equal(Math.Round(0.35 * hs.HvTotalCapacityKwh!.Value, 1), hs.HvSocKwh);
        Assert.Equal((true, false, zsBefore.EvSocPercent), (zs.IsLocked, zs.DriverDoorOpen, zs.EvSocPercent));
    }

    [Fact]
    public async Task EachCar_HasTripsOfItsOwn_AndAVisitorsChangesStayWithIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var trips = new DemoTripRepository();

        var zsLog = await trips.GetLogAsync(Zs.Id, From, To, ct);
        var hsLog = await trips.GetLogAsync(Hs.Id, From, To, ct);
        Assert.True(hsLog.Count >= 10);
        Assert.Empty(zsLog.Select(e => e.Id).Intersect(hsLog.Select(e => e.Id)));
        Assert.Empty(zsLog.Select(e => e.StartedAt).Intersect(hsLog.Select(e => e.StartedAt)));

        await trips.SetHiddenAsync(Hs.Id, hsLog[0].Id, true, ct);

        Assert.True((await trips.GetLogAsync(Hs.Id, From, To, ct))[0].IsHidden);
        Assert.DoesNotContain(await trips.GetLogAsync(Zs.Id, From, To, ct), e => e.IsHidden);
    }

    // The ZS's timeline ends with the trip it is driving; the parked HS's with the trip that brought
    // it home, which ends where the car stands now.
    [Fact]
    public async Task TheLatestTrip_IsTheOneUnderWay_OrTheOneThatBroughtTheParkedCarHome()
    {
        var ct = TestContext.Current.CancellationToken;
        var trips = new DemoTripRepository();
        var telemetry = new DemoTelemetryRepository();

        var zs = (await trips.GetLatestAsync(Zs.Id, DateTime.UtcNow, ct))!;
        var hs = (await trips.GetLatestAsync(Hs.Id, DateTime.UtcNow, ct))!;
        var parkedAt = (await telemetry.GetLatestPositionAsync(Hs.Id, ct))!;

        Assert.Null(zs.Id);
        Assert.NotNull(hs.Id);
        Assert.Equal((parkedAt.Latitude, parkedAt.Longitude), (hs.Points[^1].Latitude, hs.Points[^1].Longitude));
    }

    // A car does not jump across the map: each trip, the one under way included, leaves from where
    // the one before it ended, and no two overlap. The one gap the ZS keeps on purpose is the way
    // home from Amstelveen that was never recorded, so the route editor has a start to suggest.
    [Theory]
    [InlineData(1, new long[] { 5 })]
    [InlineData(2, new long[0])]
    public async Task EachTrip_LeavesFromWhereTheOneBeforeItEnded(int vehicleId, long[] leftFromElsewhere)
    {
        var ct = TestContext.Current.CancellationToken;
        var trips = await new DemoTripRepository().GetTripsAsync(vehicleId, From, To, ct);

        Assert.True(trips.Count >= 10);
        List<long?> jumps = [];
        for (var i = 1; i < trips.Count; i++)
        {
            var (before, trip) = (trips[i - 1], trips[i]);
            Assert.True(trip.StartedAt >= before.EndedAt, $"Trip {trip.Id} starts before trip {before.Id} ended");
            var gapKm = GeoHelper.Haversine(before.Points[^1].Latitude, before.Points[^1].Longitude, trip.Points[0].Latitude, trip.Points[0].Longitude);
            if (gapKm > 0.05) jumps.Add(trip.Id);
        }

        Assert.Equal(leftFromElsewhere.Cast<long?>(), jumps);
    }

    // Over the weeks the map shows, the ZS charges where its trips leave it: at home, on its wall box.
    // A fast charge somewhere it never drove to would put it in two places at once.
    [Fact]
    public async Task OverTheTripWeeks_TheZsChargesOnlyAtHome()
    {
        var ct = TestContext.Current.CancellationToken;
        var log = await new DemoTripRepository().GetLogAsync(Zs.Id, From, To, ct);
        var home = (log[0].StartLatitude, log[0].StartLongitude);

        var sessions = await new DemoTelemetryRepository().GetChargeSessionsAsync(Zs.Id, log[0].StartedAt.Date, To, ct);

        Assert.NotEmpty(sessions);
        Assert.All(sessions, s => Assert.Equal("AC", s.ChargingType));
        Assert.All(sessions, s => Assert.True(GeoHelper.Haversine(s.Latitude!.Value, s.Longitude!.Value, home.StartLatitude, home.StartLongitude) < 0.5));
    }

    // A plug-in hybrid charges at home every evening it needs to, on a slower wall box, and never
    // at a fast charger.
    [Fact]
    public async Task TheHs_ChargesAtHomeOnly_MoreOften_AndSlower()
    {
        var ct = TestContext.Current.CancellationToken;
        var telemetry = new DemoTelemetryRepository();
        var home = (await telemetry.GetLatestPositionAsync(Hs.Id, ct))!;

        var hs = await telemetry.GetChargeSessionsAsync(Hs.Id, From, To, ct);
        var zs = await telemetry.GetChargeSessionsAsync(Zs.Id, From, To, ct);

        Assert.True(hs.Count > zs.Count);
        Assert.All(hs, s => Assert.Equal("AC", s.ChargingType));
        Assert.All(hs, s => Assert.InRange(s.MaxPowerKw.GetValueOrDefault(), 3, 4.5));
        Assert.All(hs, s => Assert.True(GeoHelper.Haversine(s.Latitude!.Value, s.Longitude!.Value, home.Latitude, home.Longitude) < 0.5));
    }

    // Around town the HS runs on its battery and its tank stays as it was; the trip to Den Bosch
    // and back is longer than the battery reaches, so the rest of it is driven on fuel.
    [Fact]
    public async Task TheHs_BurnsFuelOnlyBeyondWhatItsBatteryReaches()
    {
        var ct = TestContext.Current.CancellationToken;
        var telemetry = new DemoTelemetryRepository();
        var trips = await new DemoTripRepository().GetLogAsync(Hs.Id, From, To, ct);
        var history = await telemetry.StreamAllAsync(Hs.Id, ct: ct).ToListAsync(ct);

        double? FuelOver(long firstTrip, long lastTrip)
        {
            var leaves = trips.Single(t => t.Id == firstTrip).StartedAt;
            var back = trips.Single(t => t.Id == lastTrip).EndedAt;
            var before = history.Last(s => s.RecordedAt < leaves).FuelLevelPercent;
            var after = history.First(s => s.RecordedAt > back).FuelLevelPercent;
            return before - after;
        }

        Assert.Equal(0, FuelOver(109, 110));
        Assert.True(FuelOver(105, 106) > 0);
    }

    // The ZS's slow puncture is its own: the HS's tyres are all where they should be.
    [Fact]
    public async Task OnlyTheZs_HasASlowPuncture()
    {
        var ct = TestContext.Current.CancellationToken;
        var telemetry = new DemoTelemetryRepository();

        var zs = (await telemetry.GetMergedLatestAsync(Zs.Id, ct))!;
        var hs = (await telemetry.GetMergedLatestAsync(Hs.Id, ct))!;

        Assert.True(zs.TyrePressureRearLeft - zs.TyrePressureRearRight > 0.2);
        Assert.Equal(hs.TyrePressureRearLeft, hs.TyrePressureRearRight);
    }

    [Fact]
    public async Task TheSeeder_GivesEachCarItsOwnMaintenanceSchedulesAndWeeklySummary_AndTheZsTheRdwCard()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        await DemoSeeder.SeedAsync(db, rdwEnabled: true, new DemoOptions(SecondVehicle: true), ct);

        var maintenance = await db.MaintenanceItems.ToListAsync(ct);
        Assert.Contains(maintenance, m => m.VehicleId == Zs.Id);
        Assert.Contains(maintenance, m => m.VehicleId == Hs.Id);
        Assert.Empty(maintenance.Where(m => m.VehicleId == Zs.Id).Select(m => m.Name)
            .Intersect(maintenance.Where(m => m.VehicleId == Hs.Id).Select(m => m.Name)));
        Assert.Contains(await db.ClimateSchedules.ToListAsync(ct), s => s.VehicleId == Hs.Id);
        Assert.Equal([Zs.Id], await db.RdwVehicleInfos.Select(r => r.VehicleId).ToListAsync(ct));
        var summaries = await db.AppNotifications.Where(n => n.Category == NotificationCategories.WeeklySummary).ToListAsync(ct);
        Assert.All(summaries, s => Assert.Contains(s.VehicleId, new int?[] { Zs.Id, Hs.Id }));
        Assert.Equal(summaries.Count, summaries.Select(s => s.VehicleId).Distinct().Count());
    }

    [Fact]
    public async Task TheSeeder_LeavesTheSecondCarOut_WhenItIsOff()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        await DemoSeeder.SeedAsync(db, rdwEnabled: true, new DemoOptions(SecondVehicle: false), ct);

        Assert.Equal([Zs.Id], await db.Vehicles.Select(v => v.Id).ToListAsync(ct));
        Assert.DoesNotContain(await db.MaintenanceItems.ToListAsync(ct), m => m.VehicleId == Hs.Id);
        Assert.DoesNotContain(await db.ClimateSchedules.ToListAsync(ct), s => s.VehicleId == Hs.Id);
    }
}
