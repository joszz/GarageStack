using GarageStack.Data.Demo;

namespace GarageStack.Tests;

public class DemoChargeSessionTests
{
    private static readonly DateTime To = DateTime.UtcNow;
    private static readonly DateTime From = To.AddDays(-366);

    [Fact]
    public async Task GetChargeSessions_GeneratesPlausibleSessionsWithVariableChargingProfiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new DemoTelemetryRepository();
        var sessions = await repo.GetChargeSessionsAsync(1, From, To, ct);
        var odometerNow = (await repo.GetMergedLatestAsync(1, ct))!.OdometerKm;

        Assert.InRange(sessions.Count, 50, 199);
        Assert.Contains(sessions, session => session.ChargingType == "AC");
        Assert.Contains(sessions, session => session.ChargingType == "DC");
        var fastCharges = sessions.Where(session => session.ChargingType == "DC").ToArray();
        Assert.True(fastCharges.Length < sessions.Count / 4);
        Assert.All(sessions, session =>
        {
            Assert.NotNull(session.EndedAt);
            var endedAt = session.EndedAt.Value;
            Assert.InRange(
                endedAt - session.StartedAt,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromHours(10));
            Assert.True(
                session.StartSocPercent is { } startSoc &&
                session.EndSocPercent is { } endSoc &&
                endSoc > startSoc);
            Assert.InRange(session.BatteryEnergyGainedKwh.GetValueOrDefault(), 1, 50);
            Assert.InRange(session.OdometerKm.GetValueOrDefault(), 1, odometerNow.GetValueOrDefault());
            Assert.True(session.Latitude is >= 51 and <= 53);
            Assert.True(session.Longitude is >= 4 and <= 6);
        });

        // The gateway publishes power in kW, and the demo does the same.
        Assert.All(sessions.Where(session => session.ChargingType == "AC"), session =>
            Assert.InRange(session.MaxPowerKw.GetValueOrDefault(), 6, 8));
        Assert.All(fastCharges, session =>
            Assert.InRange(session.MaxPowerKw.GetValueOrDefault(), 40, 90));
    }

    [Fact]
    public async Task GetChargeSessions_KeepsOnlyThePeriod()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new DemoTelemetryRepository();
        var month = await repo.GetChargeSessionsAsync(1, To.AddDays(-30), To, ct);
        var year = await repo.GetChargeSessionsAsync(1, From, To, ct);

        Assert.NotEmpty(month);
        Assert.True(month.Count < year.Count);
        Assert.All(month, session => Assert.True(session.EndedAt >= To.AddDays(-30)));
    }

    [Fact]
    public async Task GetChargePower_GivesTheCurveOfOneSession()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new DemoTelemetryRepository();
        var sessions = await repo.GetChargeSessionsAsync(1, From, To, ct);

        var ac = sessions.First(session => session.ChargingType == "AC");
        var acCurve = await repo.GetChargePowerAsync(1, ac.StartedAt, ac.EndedAt!.Value, ct);
        Assert.True(acCurve.Count >= 3);
        Assert.All(acCurve, point => Assert.InRange(point.RecordedAt, ac.StartedAt, ac.EndedAt.Value));
        Assert.True(acCurve[0].PowerKw < acCurve[1].PowerKw);
        Assert.Equal(ac.MaxPowerKw, acCurve.Max(point => point.PowerKw));

        var dc = sessions.First(session => session.ChargingType == "DC");
        var dcCurve = await repo.GetChargePowerAsync(1, dc.StartedAt, dc.EndedAt!.Value, ct);
        Assert.True(dcCurve[0].PowerKw > dcCurve[^1].PowerKw);
    }
}
