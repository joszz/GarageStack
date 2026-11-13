using GarageStack.Data.Demo;

namespace GarageStack.Tests;

public class DemoChargeSessionTests
{
    [Fact]
    public async Task GetChargeSessions_GeneratesPlausibleSessionsWithVariableChargingProfiles()
    {
        var sessions = await new DemoTelemetryRepository().GetChargeSessionsAsync(
            1,
            TestContext.Current.CancellationToken);

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
                TimeSpan.FromHours(6));
            Assert.True(
                session.StartSocPercent is { } startSoc &&
                session.EndSocPercent is { } endSoc &&
                endSoc > startSoc);
            Assert.InRange(session.BatteryEnergyGainedKwh.GetValueOrDefault(), 1, 50);
            Assert.True(session.OdometerKm is > 24300);
            Assert.True(session.Latitude is >= 51 and <= 53);
            Assert.True(session.Longitude is >= 4 and <= 6);
            Assert.True(session.PowerPoints.Count >= 2);
            Assert.All(session.PowerPoints, point =>
                Assert.InRange(point.RecordedAt, session.StartedAt, endedAt));
        });

        Assert.All(sessions.Where(session => session.ChargingType == "AC"), session =>
        {
            Assert.NotNull(session.MaxPowerKw);
            Assert.InRange(session.MaxPowerKw.Value, 6, 8);
            Assert.True(session.PowerPoints.Count >= 3);
            Assert.True(session.PowerPoints[0].PowerKw < session.PowerPoints[1].PowerKw);
            Assert.True(session.PowerPoints.Skip(1).Select(point => point.PowerKw).Distinct().Count() > 1);
        });
        Assert.All(fastCharges, session =>
        {
            Assert.NotNull(session.MaxPowerKw);
            Assert.InRange(session.MaxPowerKw.Value, 40, 90);
            Assert.True(session.PowerPoints[0].PowerKw > session.PowerPoints[^1].PowerKw);
        });
    }
}
