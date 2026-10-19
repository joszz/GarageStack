using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Api.Services;

/// <summary>
/// Everything GarageStack keeps, in one zip: per car its trips, maintenance with its history,
/// climate schedules, commute places and every telemetry row, and besides those the notifications
/// and the account's settings. Telemetry is a CSV a spreadsheet opens; the rest is JSON.
/// </summary>
internal sealed class DataExport(
    IVehicleRepository vehicles,
    ITelemetryRepository telemetry,
    ITripRepository trips,
    IMaintenanceRepository maintenance,
    IClimateScheduleRepository schedules,
    ICommutePlaceRepository places,
    INotificationRepository notifications,
    IUserSettingsRepository settings)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    // Every column of a telemetry row, in declaration order; the row's car is the folder it is in.
    private static readonly PropertyInfo[] TelemetryColumns =
        [.. typeof(TelemetrySnapshot).GetProperties().Where(p => p.Name != nameof(TelemetrySnapshot.Vehicle) && IsScalar(p.PropertyType))];

    // Further back than any car GarageStack has ever recorded, and forward past today.
    private static readonly DateTime Beginning = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Writes the zip to <paramref name="target"/>, which stays open.</summary>
    public async Task WriteAsync(Stream target, string? accountKey, CancellationToken ct)
    {
        using var zip = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true);
        var cars = await vehicles.GetAllAsync(ct);
        var end = DateTime.UtcNow.AddDays(1);

        await WriteJsonAsync(zip, "export.json", new
        {
            exportedAt = DateTime.UtcNow,
            version = typeof(DataExport).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            vehicles = cars.Select(c => c.Vin),
            contents = new Dictionary<string, string>
            {
                ["settings.json"] = "The settings of the account that made the export, by section",
                ["notifications.json"] = "The notifications in the bell, archived ones included",
                ["vehicles/{vin}/vehicle.json"] = "The car as GarageStack knows it",
                ["vehicles/{vin}/trips.json"] = "Every saved trip, with its places, odometer readings, purpose and notes",
                ["vehicles/{vin}/maintenance.json"] = "Maintenance items, each with its service history",
                ["vehicles/{vin}/climate-schedules.json"] = "Climate schedules",
                ["vehicles/{vin}/commute-places.json"] = "Home and work places",
                ["vehicles/{vin}/telemetry.csv"] = "Every telemetry row, oldest first, as recorded",
            },
        }, ct);

        if (accountKey is not null)
            await WriteJsonAsync(zip, "settings.json", await settings.GetAsync(accountKey, ct), ct);
        await WriteJsonAsync(zip, "notifications.json", await notifications.ListAsync(int.MaxValue, ct), ct);

        foreach (var car in cars)
        {
            var folder = $"vehicles/{car.Vin}";
            await WriteJsonAsync(zip, $"{folder}/vehicle.json", new
            {
                car.Vin,
                car.Model,
                car.LicensePlate,
                vehicleType = VehicleTypeHelper.GetVehicleType(car),
                car.CreatedAt,
                car.LastParkedAt,
                car.ParkedLatitude,
                car.ParkedLongitude,
            }, ct);
            await WriteJsonAsync(zip, $"{folder}/trips.json", await trips.GetLogAsync(car.Id, Beginning, end, ct), ct);

            var items = await maintenance.ListAsync(car.Id, ct);
            var withHistory = new List<object>(items.Count);
            foreach (var item in items)
            {
                var history = await maintenance.GetLogAsync(car.Id, item.Id, ct) ?? [];
                withHistory.Add(new
                {
                    item.Id,
                    item.Name,
                    item.Notes,
                    item.IntervalKm,
                    item.IntervalMonths,
                    item.LastServiceDate,
                    item.LastServiceOdometerKm,
                    item.CreatedAt,
                    history = history.Select(h => new { h.PerformedAt, h.OdometerKm, h.Notes, h.CreatedAt }),
                });
            }
            await WriteJsonAsync(zip, $"{folder}/maintenance.json", withHistory, ct);

            var carSchedules = await schedules.ListAsync(car.Id, ct);
            await WriteJsonAsync(zip, $"{folder}/climate-schedules.json", carSchedules.Select(s => new
            {
                s.Id,
                s.Name,
                s.Enabled,
                s.StartTime,
                s.Days,
                s.TimeZoneId,
                s.Mode,
                s.TemperatureC,
                s.RearDefroster,
                s.SeatLeftLevel,
                s.SeatRightLevel,
                s.OnlyBelowC,
                s.OnlyAboveC,
            }), ct);

            var carPlaces = await places.ListAsync(car.Id, ct);
            await WriteJsonAsync(zip, $"{folder}/commute-places.json", carPlaces.Select(p => new
            {
                p.Kind,
                p.Label,
                p.Latitude,
                p.Longitude,
                p.RadiusMeters,
            }), ct);

            await WriteTelemetryAsync(zip, $"{folder}/telemetry.csv", car.Id, ct);
        }
    }

    private static async Task WriteJsonAsync<T>(ZipArchive zip, string name, T value, CancellationToken ct)
    {
        await using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        await JsonSerializer.SerializeAsync(entry, value, Json, ct);
    }

    private async Task WriteTelemetryAsync(ZipArchive zip, string name, int vehicleId, CancellationToken ct)
    {
        await using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        await using var csv = new StreamWriter(entry, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await csv.WriteLineAsync(string.Join(',', TelemetryColumns.Select(c => c.Name)));
        await foreach (var row in telemetry.StreamAllAsync(vehicleId, ct))
            await csv.WriteLineAsync(string.Join(',', TelemetryColumns.Select(c => Cell(c.GetValue(row)))));
    }

    // Numbers and times as any spreadsheet or script reads them, whatever the server's language.
    private static string Cell(object? value) => value switch
    {
        null => "",
        DateTime time => time.ToString("O", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        string text => Quote(text),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => Quote(value.ToString() ?? ""),
    };

    private static string Quote(string text) =>
        text.IndexOfAny([',', '"', '\n', '\r']) < 0 ? text : $"\"{text.Replace("\"", "\"\"")}\"";

    private static bool IsScalar(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime);
    }
}
