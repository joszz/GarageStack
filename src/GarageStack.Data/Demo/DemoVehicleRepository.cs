using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

public sealed class DemoVehicleRepository : IVehicleRepository
{
    public static readonly Vehicle DemoVehicle = new()
    {
        Id = 1,
        Vin = "DEMOVIN0000012345",
        Model = "MG ZS EV",
        Series = "ZS EV",
        SaicUser = "demo@example.com",
        CreatedAt = DateTime.UtcNow.AddMonths(-6),
        // BType 1: a ZS EV takes a charge target, so the demo shows that control.
        ConfigJson = """{"hw_version":"MG_BEV_1.0","BType":"1"}""",
        // A made-up plate in the current Dutch format; the demo's RDW client answers for any plate.
        LicensePlate = "ZZ123Z",
    };

    /// <summary>
    /// A second car, a plug-in hybrid, for trying the car picker. Only there when DEMO_SECOND_VEHICLE
    /// is true, so the public demo keeps its one car. It shares the first car's telemetry.
    /// </summary>
    public static readonly Vehicle SecondDemoVehicle = new()
    {
        Id = 2,
        Vin = "DEMOVIN0000067890",
        Model = "MG HS PHEV",
        Series = "HS",
        SaicUser = "demo@example.com",
        CreatedAt = DateTime.UtcNow.AddMonths(-3),
        ConfigJson = """{"hw_version":"AS23 PHEV","BType":"0"}""",
    };

    public static bool SecondVehicleEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("DEMO_SECOND_VEHICLE"), "true", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<Vehicle> All =>
        SecondVehicleEnabled ? [DemoVehicle, SecondDemoVehicle] : [DemoVehicle];

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
}
