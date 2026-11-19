using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data.Demo;

namespace GarageStack.Tests;

public class DemoChargeSessionTests
{
    private static readonly string? Capture = FindCapture();
    private static readonly DateTime To = CaptureEnd(Capture) ?? DateTime.UtcNow;
    private static readonly DateTime From = To.AddDays(-366);

    private static string? FindCapture()
    {
        var configuredPath = Environment.GetEnvironmentVariable("DEMO_TELEMETRY_CSV");
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return File.Exists(configuredPath) ? configuredPath : null;

        var telemetryDirectory = RepositoryFiles.PathOf("telemetry");
        var canonicalFile = Path.Combine(telemetryDirectory, "telemetry.csv");
        if (File.Exists(canonicalFile)) return canonicalFile;
        if (!Directory.Exists(telemetryDirectory)) return null;
        var captures = Directory.GetFiles(telemetryDirectory, "telemetry.csv", SearchOption.AllDirectories);
        return captures.Length == 1 ? captures[0] : null;
    }

    private static DateTime? CaptureEnd(string? capture)
    {
        if (capture is null) return null;
        var lines = File.ReadLines(capture).ToArray();
        var headers = lines[0].Split(',');
        var timestampColumn = Array.IndexOf(headers, nameof(TelemetrySnapshot.RecordedAt));
        return DateTime.Parse(
            lines[^1].Split(',')[timestampColumn],
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
    }

    [Fact]
    public async Task GetMergedLatest_UsesTheLocalCaptureWhenPresent()
    {
        if (Capture is null) return;

        var lines = File.ReadLines(Capture).ToArray();
        var headers = lines[0].Split(',');
        var latestRow = lines[^1].Split(',');
        var expectedRecordedAt = DateTime.Parse(
            latestRow[Array.IndexOf(headers, nameof(TelemetrySnapshot.RecordedAt))],
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        var odometerColumn = Array.IndexOf(headers, nameof(TelemetrySnapshot.OdometerKm));
        var expectedOdometer = double.Parse(
            lines.Skip(1).Reverse()
                .Select(line => line.Split(','))
                .First(cells => cells[odometerColumn].Length > 0)[odometerColumn],
            System.Globalization.CultureInfo.InvariantCulture);

        var snapshot = await new DemoTelemetryRepository().GetMergedLatestAsync(1, TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(expectedRecordedAt, snapshot.RecordedAt);
        Assert.Equal(expectedOdometer, snapshot.OdometerKm.GetValueOrDefault());
    }

    [Fact]
    public async Task GetChargeSessions_UsesCaptureOrGeneratedChargingTelemetry()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new DemoTelemetryRepository();
        var sessions = await repo.GetChargeSessionsAsync(1, From, To, ct);
        Assert.NotEmpty(sessions);
        Assert.All(sessions, session =>
        {
            Assert.NotNull(session.EndedAt);
            Assert.True(session.EndedAt > session.StartedAt);
            Assert.InRange(session.StartedAt, From - ChargeSessionHistory.SessionLookaround, To);
        });
    }

    [Fact]
    public async Task GetChargeSessions_KeepsOnlyThePeriod()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new DemoTelemetryRepository();
        var week = await repo.GetChargeSessionsAsync(1, To.AddDays(-7), To, ct);
        var year = await repo.GetChargeSessionsAsync(1, From, To, ct);

        Assert.True(week.Count <= year.Count);
        Assert.All(week, session => Assert.True(session.EndedAt >= To.AddDays(-7)));
    }

    [Fact]
    public async Task GetChargePower_GivesTheCurveOfOneSession()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new DemoTelemetryRepository();
        var sessions = await repo.GetChargeSessionsAsync(1, From, To, ct);
        Assert.NotEmpty(sessions);
        var session = sessions.OrderByDescending(item => item.MaxPowerKw).First();
        var curve = await repo.GetChargePowerAsync(1, session.StartedAt, session.EndedAt!.Value, ct);
        Assert.NotEmpty(curve);
        Assert.All(curve, point => Assert.InRange(point.RecordedAt, session.StartedAt, session.EndedAt.Value));
        Assert.Equal(session.MaxPowerKw.GetValueOrDefault(), curve.Max(point => point.PowerKw));
    }

    [Fact]
    public async Task MissingCapture_UsesTheOriginalMockVehicleAndTrips()
    {
        if (Capture is not null) return;

        var ct = TestContext.Current.CancellationToken;
        var telemetry = await new DemoTelemetryRepository().GetMergedLatestAsync(1, ct);
        var trips = new DemoTripRepository();
        var summaries = await trips.GetSummariesAsync(1, DateTime.MinValue, DateTime.MaxValue, ct);

        Assert.Equal(24852, telemetry?.OdometerKm);
        Assert.NotEmpty(summaries);
    }
}
