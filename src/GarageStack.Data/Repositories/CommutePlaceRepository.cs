using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data.Repositories;

public class CommutePlaceRepository(AppDbContext db) : ICommutePlaceRepository
{
    public async Task<IReadOnlyList<CommutePlace>> ListAsync(int vehicleId, CancellationToken ct = default) =>
        await db.CommutePlaces.AsNoTracking()
            .Where(p => p.VehicleId == vehicleId)
            .OrderBy(p => p.Kind).ThenBy(p => p.Id)
            .ToListAsync(ct);

    public Task<int> CountAsync(int vehicleId, CancellationToken ct = default) =>
        db.CommutePlaces.CountAsync(p => p.VehicleId == vehicleId, ct);

    public async Task<CommutePlace> AddAsync(CommutePlace place, CancellationToken ct = default)
    {
        db.CommutePlaces.Add(place);
        await db.SaveChangesAsync(ct);
        return place;
    }

    public async Task<CommutePlace?> UpdateAsync(int vehicleId, int id, Action<CommutePlace> change, CancellationToken ct = default)
    {
        var place = await db.CommutePlaces.FirstOrDefaultAsync(p => p.Id == id && p.VehicleId == vehicleId, ct);
        if (place is null) return null;

        change(place);
        await db.SaveChangesAsync(ct);
        return place;
    }

    public async Task<bool> DeleteAsync(int vehicleId, int id, CancellationToken ct = default)
    {
        var place = await db.CommutePlaces.FirstOrDefaultAsync(p => p.Id == id && p.VehicleId == vehicleId, ct);
        if (place is null) return false;

        db.CommutePlaces.Remove(place);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
