using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

public sealed class DemoVehicleRepository(DemoOptions options) : IVehicleRepository
{
    public static readonly Vehicle DemoVehicle = new()
    {
        Id = 1,
        Vin = "DEMOVIN0000012345",
        Model = "MG ZS EV",
        Series = "ZS EV",
        SaicUser = "demo@example.com",
        CreatedAt = DateTime.UtcNow.AddMonths(-6),
        // BType 1: a ZS EV takes a charge target, so the demo shows that control. Four doors and
        // no sunroof (S35 0), as most cars report.
        ConfigJson = """{"hw_version":"MG_BEV_1.0","BType":"1","DOOR":"1111","S35":"0"}""",
        // A made-up plate in the current Dutch format; the demo's RDW client answers for any plate.
        LicensePlate = "ZZ123Z",
    };

    /// <summary>
    /// A second car, a plug-in hybrid, for trying the car picker. Only there when
    /// <see cref="DemoOptions.SecondVehicle"/> is on, so the public demo keeps its one car. It shares
    /// the first car's telemetry.
    /// </summary>
    public static readonly Vehicle SecondDemoVehicle = new()
    {
        Id = 2,
        Vin = "DEMOVIN0000067890",
        Model = "MG HS PHEV",
        Series = "HS",
        SaicUser = "demo@example.com",
        CreatedAt = DateTime.UtcNow.AddMonths(-3),
        // BType 2: it plugs in, as a real plug-in hybrid reports (0 is a plain hybrid), but takes
        // no charge target, so the demo shows a car without that control too. CTRLBOOT 2 reports a
        // power tailgate, so its controls show without switching it on in the settings.
        ConfigJson = """{"hw_version":"AS23 PHEV","BType":"2","DOOR":"1111","S35":"0","CTRLBOOT":"2"}""",
    };

    private IReadOnlyList<Vehicle> All => options.Vehicles;

    public Task<IReadOnlyList<Vehicle>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult(All);

    public Task<Vehicle?> GetByVinAsync(string vin, CancellationToken ct = default) =>
        Task.FromResult(All.FirstOrDefault(v => v.Vin == vin));

    public Task<Vehicle> GetOrCreateByVinAsync(string vin, string? saicUser = null, CancellationToken ct = default) =>
        Task.FromResult(DemoVehicle);

    public Task SetModelAsync(int vehicleId, string model, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SetConfigValueAsync(int vehicleId, string key, string value, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SetParkedAsync(
        int vehicleId, DateTime parkedAt, double? latitude, double? longitude, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SetLastMessageIdAsync(int vehicleId, string messageId, CancellationToken ct = default) =>
        Task.CompletedTask;

    // Kept, unlike the gateway's own updates: entering a plate is how the RDW card is tried out.
    public Task SetLicensePlateAsync(int vehicleId, string? licensePlate, CancellationToken ct = default)
    {
        var vehicle = All.FirstOrDefault(v => v.Id == vehicleId);
        if (vehicle is not null) vehicle.LicensePlate = licensePlate;
        return Task.CompletedTask;
    }

    // Kept too: switching it on is how the tailgate controls are tried out.
    public Task SetHasPowerTailgateAsync(int vehicleId, bool hasPowerTailgate, CancellationToken ct = default)
    {
        var tailgateCar = All.FirstOrDefault(v => v.Id == vehicleId);
        if (tailgateCar is not null) tailgateCar.HasPowerTailgate = hasPowerTailgate;
        return Task.CompletedTask;
    }

    // Kept in memory as well, so the vehicle settings can be tried out; a restart forgets them.
    public Task SetSettingsAsync(int vehicleId, VehicleSettings settings, CancellationToken ct = default)
    {
        var vehicle = All.FirstOrDefault(v => v.Id == vehicleId);
        if (vehicle is not null) vehicle.Settings = settings.Copy();
        return Task.CompletedTask;
    }
}
