using System.Collections.Concurrent;
using GarageStack.Core.Interfaces;

namespace GarageStack.Worker.Mqtt;

/// <summary>
/// Opens a DI scope for one message's database work and finds the vehicle the message is about,
/// creating it on first sight. Every MQTT message names its VIN and a poll is several messages, so
/// the id is remembered per VIN with the account it was last seen under; a message under another
/// account goes through <see cref="IVehicleRepository.GetOrCreateByVinAsync"/> again, which
/// records the account.
/// </summary>
public sealed class VehicleResolver(IServiceScopeFactory scopeFactory)
{
    private readonly ConcurrentDictionary<string, (int VehicleId, string? SaicUser)> _vehicleIds = new();

    /// <returns>
    /// The open scope and the vehicle's id. Disposing the result disposes the scope, so the caller
    /// can resolve further scoped services (the telemetry repository, the database) from it first.
    /// </returns>
    public async Task<ResolvedVehicle> ResolveAsync(string vin, string? saicUser, CancellationToken ct)
    {
        var scope = scopeFactory.CreateScope();
        try
        {
            var vehicles = scope.ServiceProvider.GetRequiredService<IVehicleRepository>();
            if (_vehicleIds.TryGetValue(vin, out var known) && (saicUser is null || saicUser == known.SaicUser))
                return new ResolvedVehicle(scope, vehicles, known.VehicleId);

            var vehicle = await vehicles.GetOrCreateByVinAsync(vin, saicUser, ct);
            _vehicleIds[vin] = (vehicle.Id, vehicle.SaicUser);
            return new ResolvedVehicle(scope, vehicles, vehicle.Id);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}

/// <summary>A vehicle found for one message, with the DI scope its database work runs in.</summary>
public sealed record ResolvedVehicle(IServiceScope Scope, IVehicleRepository Vehicles, int VehicleId) : IDisposable
{
    public IServiceProvider Services => Scope.ServiceProvider;

    public void Dispose() => Scope.Dispose();
}
