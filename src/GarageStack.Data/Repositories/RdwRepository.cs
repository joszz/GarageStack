using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data.Repositories;

public class RdwRepository(AppDbContext db) : IRdwRepository
{
    public Task<RdwVehicleInfo?> GetAsync(int vehicleId, CancellationToken ct = default) =>
        db.RdwVehicleInfos.AsNoTracking().FirstOrDefaultAsync(r => r.VehicleId == vehicleId, ct);

    public async Task<IReadOnlyList<RdwVehicleInfo>> ListAsync(CancellationToken ct = default) =>
        await db.RdwVehicleInfos.AsNoTracking().OrderBy(r => r.VehicleId).ToListAsync(ct);

    public async Task SaveAsync(RdwVehicleInfo info, CancellationToken ct = default)
    {
        var row = await TrackedOrNewAsync(info.VehicleId, ct);
        var recallsJson = row.LicensePlate == info.LicensePlate
            ? KeepNotified(info.RecallsJson, row.RecallsJson)
            : info.RecallsJson;
        row.LicensePlate = info.LicensePlate;
        row.Found = info.Found;
        row.Make = info.Make;
        row.Model = info.Model;
        row.ApkExpiresOn = info.ApkExpiresOn;
        row.WamInsured = info.WamInsured;
        row.OpenRecallIndicator = info.OpenRecallIndicator;
        row.RecallsJson = recallsJson;
        row.FetchedAt = info.FetchedAt;
        row.LastError = info.LastError?.Truncate(RdwLimits.ErrorMaxLength);
        await SaveOrRetryAsync(info, ct);
    }

    public async Task SetErrorAsync(int vehicleId, string licensePlate, string error, CancellationToken ct = default)
    {
        var row = await TrackedOrNewAsync(vehicleId, ct);
        // An error about another plate than the one looked up before says nothing about the old
        // plate's data: start that plate afresh rather than show the old car's APK under it.
        if (row.LicensePlate != licensePlate)
        {
            row.LicensePlate = licensePlate;
            row.Found = false;
            row.Make = row.Model = null;
            row.ApkExpiresOn = null;
            row.WamInsured = row.OpenRecallIndicator = null;
            row.RecallsJson = "[]";
            row.FetchedAt = null;
        }
        row.LastError = error.Truncate(RdwLimits.ErrorMaxLength);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkRecallsNotifiedAsync(
        int vehicleId, IReadOnlyCollection<string> codes, DateTime notifiedAt, CancellationToken ct = default)
    {
        var row = await db.RdwVehicleInfos.FirstOrDefaultAsync(r => r.VehicleId == vehicleId, ct);
        if (row is null) return;

        var recalls = RdwRecalls.Parse(row.RecallsJson)
            .Select(r => codes.Contains(r.Code) && r.NotifiedAt is null ? r with { NotifiedAt = notifiedAt } : r)
            .ToList();
        row.RecallsJson = RdwRecalls.Serialize(recalls);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int vehicleId, CancellationToken ct = default)
    {
        var row = await db.RdwVehicleInfos.FirstOrDefaultAsync(r => r.VehicleId == vehicleId, ct);
        if (row is null) return;
        db.RdwVehicleInfos.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    // A lookup builds on the row as it read it. A push the Worker marked since then must survive the
    // save, or the next round would push that recall again.
    private static string KeepNotified(string incomingJson, string storedJson)
    {
        var notified = RdwRecalls.Parse(storedJson)
            .Where(r => r.NotifiedAt is not null)
            .ToDictionary(r => r.Code, r => r.NotifiedAt, StringComparer.Ordinal);
        if (notified.Count == 0) return incomingJson;

        return RdwRecalls.Serialize(RdwRecalls.Parse(incomingJson)
            .Select(r => r.NotifiedAt is null && notified.TryGetValue(r.Code, out var at) ? r with { NotifiedAt = at } : r)
            .ToList());
    }

    private async Task<RdwVehicleInfo> TrackedOrNewAsync(int vehicleId, CancellationToken ct)
    {
        var row = await db.RdwVehicleInfos.FirstOrDefaultAsync(r => r.VehicleId == vehicleId, ct);
        if (row is not null) return row;

        row = new RdwVehicleInfo { VehicleId = vehicleId };
        db.RdwVehicleInfos.Add(row);
        return row;
    }

    // The API and the Worker can both create a vehicle's first row at the same moment; the loser
    // of that race writes over the winner's row instead.
    private async Task SaveOrRetryAsync(RdwVehicleInfo info, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            db.ChangeTracker.Clear();
            await SaveAsync(info, ct);
        }
    }
}
