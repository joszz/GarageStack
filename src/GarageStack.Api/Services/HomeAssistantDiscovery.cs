using System.Globalization;
using System.Text.Json.Nodes;
using GarageStack.Core.Interfaces;

namespace GarageStack.Api.Services;

/// <summary>
/// What GarageStack tells Home Assistant over MQTT discovery: the <see cref="VehicleFacts"/> the
/// gateway's own discovery cannot know (the APK and recalls, the maintenance due first, the newest
/// trip). Each sensor names the car's VIN as its device identifier, which is the identifier the
/// gateway announces the car under, so the sensors join the car's existing device.
/// <para>
/// One retained state message per car on <c>garagestack/&lt;VIN&gt;/state</c> carries every value,
/// and each sensor's discovery config points into it. A sensor with nothing to show (no plate set,
/// no maintenance items, no trips yet) gets an empty retained config, which removes it.
/// </para>
/// </summary>
public static class HomeAssistantDiscovery
{
    public const string DiscoveryPrefix = "homeassistant";

    public static string StateTopic(string vin) => $"garagestack/{vin}/state";

    /// <summary>One entity: its key in the state message, the facts it needs, and how Home Assistant shows it.</summary>
    private sealed record Sensor(
        string Key,
        string Name,
        Func<VehicleFacts, bool> Applies,
        string Component = "sensor",
        string? DeviceClass = null,
        string? Unit = null,
        string? Icon = null,
        string[]? Options = null);

    private static readonly Sensor[] Sensors =
    [
        new("apk_expires_on", "APK expires", f => f.HasRdw, DeviceClass: "date"),
        new("apk_days_left", "APK days left", f => f.HasRdw, Unit: "d", Icon: "mdi:calendar-clock"),
        new("open_recalls", "Open recalls", f => f.HasRdw, Icon: "mdi:alert-circle-outline"),
        new("wam_insured", "WAM insured", f => f.HasRdw, Component: "binary_sensor", Icon: "mdi:shield-car"),
        new("next_maintenance", "Next maintenance", f => f.HasMaintenance, Icon: "mdi:wrench-clock"),
        new("next_maintenance_status", "Next maintenance status", f => f.HasMaintenance, DeviceClass: "enum",
            Icon: "mdi:wrench", Options: ["ok", "due_soon", "overdue", "unknown"]),
        new("next_maintenance_due", "Next maintenance due", f => f.HasMaintenance, DeviceClass: "date"),
        new("next_maintenance_days_left", "Next maintenance days left", f => f.HasMaintenance, Unit: "d", Icon: "mdi:calendar-clock"),
        new("next_maintenance_km_left", "Next maintenance distance left", f => f.HasMaintenance, DeviceClass: "distance", Unit: "km"),
        new("last_trip_distance", "Last trip distance", f => f.HasTrip, DeviceClass: "distance", Unit: "km"),
        new("last_trip_started", "Last trip started", f => f.HasTrip, DeviceClass: "timestamp"),
        new("last_trip_ended", "Last trip ended", f => f.HasTrip, DeviceClass: "timestamp"),
    ];

    /// <summary>Every sensor's discovery topic, with its config, or an empty payload that removes it.</summary>
    public static IReadOnlyList<(string Topic, string Payload)> Configs(string vin, VehicleFacts facts) =>
        [.. Sensors.Select(s => (ConfigTopic(vin, s), s.Applies(facts) ? Config(vin, s) : string.Empty))];

    /// <summary>The state message every sensor reads its value from.</summary>
    public static string State(VehicleFacts f) => new JsonObject
    {
        ["apk_expires_on"] = Date(f.ApkExpiresOn),
        ["apk_days_left"] = f.ApkDaysLeft,
        ["open_recalls"] = f.OpenRecalls,
        ["wam_insured"] = f.WamInsured,
        ["next_maintenance"] = f.NextMaintenance,
        // Home Assistant's enum options are snake_case; the API's dueSoon is due_soon here.
        ["next_maintenance_status"] = f.NextMaintenanceStatus is "dueSoon" ? "due_soon" : f.NextMaintenanceStatus,
        ["next_maintenance_due"] = Date(f.NextMaintenanceDueDate is { } due ? DateOnly.FromDateTime(due) : null),
        ["next_maintenance_days_left"] = f.NextMaintenanceDaysLeft,
        ["next_maintenance_km_left"] = f.NextMaintenanceKmLeft,
        ["last_trip_distance"] = f.LastTripDistanceKm,
        ["last_trip_started"] = Timestamp(f.LastTripStartedAt),
        ["last_trip_ended"] = Timestamp(f.LastTripEndedAt),
        ["last_trip_in_progress"] = f.LastTripInProgress,
    }.ToJsonString();

    private static string ConfigTopic(string vin, Sensor s) => $"{DiscoveryPrefix}/{s.Component}/garagestack_{vin}/{s.Key}/config";

    private static string Config(string vin, Sensor s)
    {
        var config = new JsonObject
        {
            ["name"] = s.Name,
            ["unique_id"] = $"garagestack_{vin}_{s.Key}",
            ["state_topic"] = StateTopic(vin),
            // A JSON null renders as none, which Home Assistant shows as unknown.
            ["value_template"] = s.Component == "binary_sensor"
                ? $"{{{{ 'ON' if value_json.{s.Key} else 'OFF' }}}}"
                : $"{{{{ value_json.{s.Key} }}}}",
            ["device"] = new JsonObject { ["identifiers"] = new JsonArray(vin) },
            ["origin"] = new JsonObject { ["name"] = "GarageStack", ["support_url"] = "https://github.com/joszz/GarageStack" },
        };
        if (s.DeviceClass is { } deviceClass) config["device_class"] = deviceClass;
        if (s.Unit is { } unit) config["unit_of_measurement"] = unit;
        if (s.Icon is { } icon) config["icon"] = icon;
        if (s.Options is { } options) config["options"] = new JsonArray([.. options.Select(o => JsonValue.Create(o))]);
        return config.ToJsonString();
    }

    private static string? Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Home Assistant wants a timestamp with its offset; the database's are UTC.
    private static string? Timestamp(DateTime? utc) =>
        utc is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture) : null;
}

/// <summary>
/// Publishes <see cref="HomeAssistantDiscovery"/> for every car every five minutes: the discovery
/// configs once per run of the API (they are retained, so Home Assistant has them whenever it
/// connects), and the state whenever it changed.
/// </summary>
public sealed class HomeAssistantPublisher(
    IServiceScopeFactory scopes,
    MqttPublisher mqtt,
    ILogger<HomeAssistantPublisher> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    // Long enough for the publisher's connection and the first telemetry after a restart.
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromSeconds(30);

    // Per VIN, what was published last: the configs (whether each sensor applied) and the state.
    private readonly Dictionary<string, (string Configs, string State)> _published = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRunDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PublishAllAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // A broker that is down only delays Home Assistant's values; it must never stop the API.
                    logger.LogWarning(ex, "Publishing to Home Assistant failed; trying again in {Minutes} minutes", Interval.TotalMinutes);
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal async Task PublishAllAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var vehicles = scope.ServiceProvider.GetRequiredService<IVehicleRepository>();
        var facts = scope.ServiceProvider.GetRequiredService<VehicleFactsService>();

        foreach (var vehicle in await vehicles.GetAllAsync(ct))
        {
            var now = await facts.GetAsync(vehicle, ct);
            var configs = HomeAssistantDiscovery.Configs(vehicle.Vin, now);
            var configsKey = string.Join('|', configs.Select(c => c.Payload.Length > 0));
            var state = HomeAssistantDiscovery.State(now);
            _published.TryGetValue(vehicle.Vin, out var last);

            if (last.Configs != configsKey)
            {
                foreach (var (topic, payload) in configs)
                    await mqtt.PublishRetainedAsync(topic, payload, ct);
            }

            if (last.Configs != configsKey || last.State != state)
                await mqtt.PublishRetainedAsync(HomeAssistantDiscovery.StateTopic(vehicle.Vin), state, ct);

            _published[vehicle.Vin] = (configsKey, state);
        }
    }
}
