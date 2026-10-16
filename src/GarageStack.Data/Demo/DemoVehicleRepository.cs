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
    };

    public Task<IReadOnlyList<Vehicle>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Vehicle>>([DemoVehicle]);

    public Task<Vehicle?> GetByVinAsync(string vin, CancellationToken ct = default) =>
        Task.FromResult<Vehicle?>(vin == DemoVehicle.Vin ? DemoVehicle : null);

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
}
