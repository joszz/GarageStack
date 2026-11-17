using System.IO.Compression;
using System.Text.Json;
using GarageStack.Api.Services;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GarageStack.Tests;

public sealed class DiagnosticsExportTests : IDisposable
{
    private const string Vin = "FAKEVN00000000001";
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly string _logs = Directory.CreateTempSubdirectory("garagestack-diagnostics-").FullName;

    public void Dispose() => Directory.Delete(_logs, recursive: true);

    private sealed class FixedHealth : HealthCheckService
    {
        public override Task<HealthReport> CheckHealthAsync(
            Func<HealthCheckRegistration, bool>? predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthReport(
                new Dictionary<string, HealthReportEntry>
                {
                    ["database"] = new(HealthStatus.Healthy, null, TimeSpan.Zero, null, null),
                    ["mqtt"] = new(HealthStatus.Unhealthy, null, TimeSpan.Zero, null, null),
                },
                TimeSpan.Zero));
    }

    private async Task<ZipArchive> WriteAsync(CancellationToken ct)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var car = new Vehicle
        {
            Vin = Vin,
            Model = "MG HS",
            Series = "AS33P S",
            SaicUser = "owner@example.com",
            LicensePlate = "ZZ123Z",
            ParkedLatitude = 52.3702157,
            ParkedLongitude = 4.8951679,
            ConfigJson = """{"hw_version":"AS33P S","BType":"1"}""",
        };
        db.Vehicles.Add(car);
        await db.SaveChangesAsync(ct);
        db.TelemetrySnapshots.AddRange(
            new TelemetrySnapshot { VehicleId = car.Id, RecordedAt = Now.UtcDateTime.AddDays(-10), MileageOfTheDay = 1 },
            new TelemetrySnapshot
            {
                VehicleId = car.Id,
                RecordedAt = Now.UtcDateTime.AddDays(-1),
                MileageOfTheDay = 2,
                Latitude = 52.3702157,
                Longitude = 4.8951679,
                Speed = 50,
            });
        await db.SaveChangesAsync(ct);

        var export = new DiagnosticsExport(
            new VehicleRepository(db),
            new TelemetryRepository(db),
            new FixedHealth(),
            new DiagnosticsFeatures(false, new Dictionary<string, bool> { ["rdw"] = true }),
            new LogDirectories(new Dictionary<string, string?> { ["api"] = _logs, ["worker"] = null }),
            new FixedTimeProvider(Now));
        var target = new MemoryStream();
        await export.WriteAsync(target, ct);
        target.Position = 0;
        return new ZipArchive(target);
    }

    private static async Task<string> ReadAsync(ZipArchive zip, string name, CancellationToken ct)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return await reader.ReadToEndAsync(ct);
    }

    [Fact]
    public async Task Write_KeepsTheLastWeekOfWarnings_Scrubbed()
    {
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(_logs, "api-20261001.log"),
            "2026-10-01 10:00:00.000 +00:00 [ERR] Too old to matter\n", ct);
        await File.WriteAllTextAsync(Path.Combine(_logs, "api-20261016.log"), string.Join('\n',
            "2026-10-16 10:00:00.000 +00:00 [WRN] Failed login attempt from IP=192.0.2.15",
            "2026-10-16 10:01:00.000 +00:00 [INF] Just information",
            "2026-10-16 10:02:00.000 +00:00 [ERR] Telemetry for ***0001 failed",
            "System.InvalidOperationException: plate ZZ123Z unknown",
            ""), ct);

        using var zip = await WriteAsync(ct);
        var logs = await ReadAsync(zip, "logs.txt", ct);

        Assert.Contains("[Warning] api: Failed login attempt from IP=[ip]", logs);
        Assert.Contains("[Error] api: Telemetry for car-1 failed", logs);
        Assert.Contains("InvalidOperationException: plate [plate] unknown", logs);
        Assert.True(logs.IndexOf("Failed login", StringComparison.Ordinal) < logs.IndexOf("Telemetry for", StringComparison.Ordinal));
        Assert.DoesNotContain("Just information", logs);
        Assert.DoesNotContain("Too old", logs);
    }

    [Fact]
    public async Task Write_KeepsTheLastWeekOfReadings_WithoutWhereTheCarWas()
    {
        var ct = TestContext.Current.CancellationToken;

        using var zip = await WriteAsync(ct);
        var lines = (await ReadAsync(zip, "cars/car-1/telemetry.csv", ct)).TrimEnd().Split('\n');

        var header = lines[0].TrimEnd('\r').Split(',');
        Assert.DoesNotContain("Latitude", header);
        Assert.DoesNotContain("Longitude", header);
        Assert.DoesNotContain("Speed", header);
        Assert.DoesNotContain("VehicleId", header);
        Assert.Equal(2, lines.Length);
        Assert.Equal("2", lines[1].TrimEnd('\r').Split(',')[Array.IndexOf(header, "MileageOfTheDay")]);
    }

    [Fact]
    public async Task Write_DescribesTheCarAndTheServer_WithoutIdentifiers()
    {
        var ct = TestContext.Current.CancellationToken;

        using var zip = await WriteAsync(ct);
        foreach (var entry in zip.Entries)
        {
            var text = await ReadAsync(zip, entry.FullName, ct);
            Assert.DoesNotContain(Vin, text);
            Assert.DoesNotContain("ZZ123Z", text);
            Assert.DoesNotContain("owner@example.com", text);
            Assert.DoesNotContain("52.37", text);
        }

        using var car = JsonDocument.Parse(await ReadAsync(zip, "cars/car-1/car.json", ct));
        Assert.Equal("phev", car.RootElement.GetProperty("vehicleType").GetString());
        Assert.Equal("AS33P S", car.RootElement.GetProperty("series").GetString());
        Assert.Equal("1", car.RootElement.GetProperty("configuration").GetProperty("BType").GetString());

        using var diagnostics = JsonDocument.Parse(await ReadAsync(zip, "diagnostics.json", ct));
        var root = diagnostics.RootElement;
        Assert.Equal("Unhealthy", root.GetProperty("health").GetProperty("checks").GetProperty("mqtt").GetString());
        Assert.True(root.GetProperty("features").GetProperty("rdw").GetBoolean());
        Assert.False(root.GetProperty("demoMode").GetBoolean());
        Assert.Equal(1, root.GetProperty("cars").GetInt32());
    }
}
