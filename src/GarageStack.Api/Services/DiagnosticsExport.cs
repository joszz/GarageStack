using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using GarageStack.Api.Endpoints;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GarageStack.Api.Services;

/// <summary>
/// What a bug report needs, in one zip that is safe to attach to a public issue: how the server
/// is set up and whether its parts are healthy, per car what GarageStack knows about it and the
/// last week of readings without where the car was, and the warnings and errors of the last week
/// from the logs. VINs become car-1, car-2, ...; plates, email addresses, IP addresses, URLs, host
/// names, sign-in names and positions are left out or replaced (<see cref="DiagnosticsRedaction"/>).
/// </summary>
internal sealed class DiagnosticsExport(
    IVehicleRepository vehicles,
    ITelemetryRepository telemetry,
    HealthCheckService health,
    GatewaySignInMonitor gatewaySignIn,
    DiagnosticsFeatures features,
    LogDirectories logs,
    TimeProvider clock)
{
    /// <summary>How far back the readings and the log entries go.</summary>
    internal static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <summary>The most log entries the zip holds, newest kept.</summary>
    internal const int MaxLogEntries = 2000;

    // How many stretches of each service's files are read looking for a week of warnings, each of
    // the log viewer's size: a chatty week at Information level can bury them deep.
    private const int MaxLogReads = 8;

    // Where the car was: a public report must not show where someone lives or drives.
    private static readonly HashSet<string> LocationColumns =
    [
        nameof(TelemetrySnapshot.Latitude),
        nameof(TelemetrySnapshot.Longitude),
        nameof(TelemetrySnapshot.Elevation),
        nameof(TelemetrySnapshot.Speed),
        nameof(TelemetrySnapshot.Heading),
        nameof(TelemetrySnapshot.VehicleId),
    ];

    /// <summary>The telemetry columns the diagnostics keep.</summary>
    internal static readonly IReadOnlyList<PropertyInfo> TelemetryColumns =
        [.. ZipExport.TelemetryColumns.Where(c => !LocationColumns.Contains(c.Name))];

    /// <summary>Writes the zip to <paramref name="target"/>, which stays open.</summary>
    public async Task WriteAsync(Stream target, CancellationToken ct)
    {
        using var zip = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true);
        var cars = (await vehicles.GetAllAsync(ct)).OrderBy(c => c.Id).ToList();
        var redaction = new DiagnosticsRedaction(cars.Select(c => (c.Vin, c.LicensePlate)));
        var now = clock.GetUtcNow();
        var since = now - Window;
        var report = await health.CheckHealthAsync(ct);

        await ZipExport.WriteJsonAsync(zip, "diagnostics.json", new
        {
            generatedAt = now.UtcDateTime,
            version = ZipExport.AppVersion,
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            timeZone = TimeZoneInfo.Local.Id,
            features.DemoMode,
            health = new
            {
                status = report.Status.ToString(),
                // The reason too: a check that threw says why in its own words, so scrubbed as well.
                checks = report.Entries.ToDictionary(e => e.Key, e => new
                {
                    status = e.Value.Status.ToString(),
                    reason = redaction.Scrub(e.Value.Description),
                }),
                // Whether the gateway is signed in to the MG account, as the Worker last said; null
                // while it has not said. Signed out, no car sends anything, whatever the checks say.
                gatewaySignedIn = gatewaySignIn.SignedIn,
            },
            features = features.Switches,
            cars = cars.Count,
            contents = new Dictionary<string, string>
            {
                ["diagnostics.json"] = "This file: the version, the platform, which optional parts are switched on, the health checks and whether the gateway is signed in to the MG account",
                ["cars/car-{n}/car.json"] = "What GarageStack knows about the car: model, series, detected drivetrain and the configuration codes the gateway reported",
                ["cars/car-{n}/telemetry.csv"] = $"Every telemetry row of the last {Window.TotalDays:0} days, oldest first, without the position, elevation, speed and heading",
                ["logs.txt"] = $"Warnings and errors of the API and the Worker from the last {Window.TotalDays:0} days, oldest first",
            },
            removed = "VINs (each car is car-1, car-2, ...), licence plates, the MG account, email and IP addresses, the host and query of URLs, host names before a port, the name and groups of a single sign-in, the account name in file paths and anything that looks like a coordinate. Trips, places, notifications and settings are not included.",
        }, ct);

        for (var i = 0; i < cars.Count; i++)
        {
            var car = cars[i];
            var folder = $"cars/{DiagnosticsRedaction.CarLabel(i)}";
            await ZipExport.WriteJsonAsync(zip, $"{folder}/car.json", new
            {
                car.Model,
                car.Series,
                vehicleType = VehicleTypeHelper.GetVehicleType(car),
                // Only the type: the rest of the car's settings are left out, as other settings are.
                vehicleTypeOverride = car.Settings?.VehicleType,
                // What the user set (null: never) and what counts, by that or else the codes.
                hasPowerTailgateSetting = car.HasPowerTailgate,
                hasPowerTailgate = VehicleEquipment.HasPowerTailgate(car),
                // Codes the gateway reports, scrubbed anyway: nothing promises what a new one holds.
                configuration = SafeJson.TryDeserialize<Dictionary<string, string>>(car.ConfigJson)?
                    .ToDictionary(c => c.Key, c => redaction.Scrub(c.Value)),
                car.CreatedAt,
                car.LastParkedAt,
                car.TripsRecordedUntil,
                car.TelemetryCompactedUntil,
            }, ct);
            await ZipExport.WriteCsvAsync(
                zip, $"{folder}/telemetry.csv", TelemetryColumns, telemetry.StreamAllAsync(car.Id, since.UtcDateTime, ct), ct);
        }

        await ZipExport.WriteTextAsync(zip, "logs.txt", ReadLogs(since).Select(e => Line(e, redaction)), ct);
    }

    // The warnings and errors since then, oldest first, at most MaxLogEntries of the newest.
    private List<LogEntry> ReadLogs(DateTimeOffset since)
    {
        var filter = new LogFilter(LogLevelName.Warning, null);
        var entries = new List<LogEntry>();
        LogCursor? before = null;
        for (var read = 0; read < MaxLogReads && entries.Count < MaxLogEntries; read++)
        {
            var page = LogFileReader.ReadPage(
                logs.Configured, filter, MaxLogEntries - entries.Count, LogEndpoints.MaxBytesPerSource, before);
            entries.AddRange(page.Entries.TakeWhile(e => e.Time >= since));
            if (page.Older is null || page.Entries.Count > 0 && page.Entries[^1].Time < since) break;
            before = page.Older;
        }
        entries.Reverse();
        return entries;
    }

    private static string Line(LogEntry entry, DiagnosticsRedaction redaction)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{entry.Time:yyyy-MM-dd HH:mm:ss.fff zzz} [{entry.Level}] {entry.Source}: {redaction.Scrub(entry.Message)}");
        return entry.Exception is null ? line : $"{line}{Environment.NewLine}{redaction.Scrub(entry.Exception)}";
    }
}
