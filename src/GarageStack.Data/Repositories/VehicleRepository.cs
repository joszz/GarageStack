using System.Text.Json;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data.Repositories;

public class VehicleRepository(AppDbContext db) : IVehicleRepository
{
    public Task<Vehicle?> GetByVinAsync(string vin, CancellationToken ct = default) =>
        db.Vehicles.FirstOrDefaultAsync(v => v.Vin == vin, ct);

    public async Task<Vehicle> GetOrCreateByVinAsync(string vin, string? saicUser = null, CancellationToken ct = default)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Vin == vin, ct);
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
        await db.SaveChangesAsync(ct);
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
