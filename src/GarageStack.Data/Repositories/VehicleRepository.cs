using System.Text.Json;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageStack.Data.Repositories;

public class VehicleRepository(AppDbContext db) : IVehicleRepository
{
    // track: false for read-only lookups so EF doesn't snapshot the entity for change detection;
    // track: true when the caller may mutate and save the returned entity.
    private Task<Vehicle?> FindByVinAsync(string vin, bool track, CancellationToken ct)
    {
        var query = track ? db.Vehicles : db.Vehicles.AsNoTracking();
        return query.FirstOrDefaultAsync(v => v.Vin == vin, ct);
    }

    public Task<Vehicle?> GetByVinAsync(string vin, CancellationToken ct = default) =>
        FindByVinAsync(vin, track: false, ct);

    public async Task<Vehicle> GetOrCreateByVinAsync(string vin, string? saicUser = null, CancellationToken ct = default)
    {
        var vehicle = await FindByVinAsync(vin, track: true, ct);
        if (vehicle is not null)
        {
            if (saicUser is not null && vehicle.SaicUser != saicUser)
            {
                vehicle.SaicUser = saicUser;
                await db.SaveChangesAsync(ct);
            }
            return vehicle;
        }

        vehicle = new Vehicle { Vin = vin, SaicUser = saicUser };
        db.Vehicles.Add(vehicle);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // The API and Worker can both call GetOrCreateByVinAsync for a brand-new VIN at
            // nearly the same time (e.g. on startup); the loser of that race hits the unique
            // constraint on Vin here. Forget our failed insert and read back the winner's row.
            db.ChangeTracker.Clear();
            vehicle = await FindByVinAsync(vin, track: true, ct)
                ?? throw new InvalidOperationException($"Unique-constraint violation on VIN {vin} but no row found on retry.");
        }
        return vehicle;
    }

    public async Task SetModelAsync(int vehicleId, string model, CancellationToken ct = default)
    {
        var vehicle = await db.Vehicles.FindAsync([vehicleId], ct);
        if (vehicle is null || vehicle.Model == model) return;
        vehicle.Model = model;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetConfigValueAsync(int vehicleId, string key, string value, CancellationToken ct = default)
    {
        var vehicle = await db.Vehicles.FindAsync([vehicleId], ct);
        if (vehicle is null) return;

        var config = vehicle.ConfigJson is not null
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(vehicle.ConfigJson) ?? []
            : new Dictionary<string, string>();

        config[key] = value;
        vehicle.ConfigJson = JsonSerializer.Serialize(config);
        await db.SaveChangesAsync(ct);
    }
}
