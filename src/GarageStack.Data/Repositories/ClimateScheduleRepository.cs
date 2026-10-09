using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Data.Repositories;

public class ClimateScheduleRepository(AppDbContext db) : IClimateScheduleRepository
{
    // A schedule is written by one person and one Worker, so a save is rarely beaten more than once.
    private const int MaxAttempts = 3;

    public async Task<IReadOnlyList<ClimateSchedule>> ListAsync(int vehicleId, CancellationToken ct = default) =>
        await db.ClimateSchedules.AsNoTracking()
            .Where(s => s.VehicleId == vehicleId)
            .OrderBy(s => s.StartTime).ThenBy(s => s.Id)
            .ToListAsync(ct);

    public Task<int> CountAsync(int vehicleId, CancellationToken ct = default) =>
        db.ClimateSchedules.CountAsync(s => s.VehicleId == vehicleId, ct);

    public async Task<ClimateSchedule> AddAsync(ClimateSchedule schedule, CancellationToken ct = default)
    {
        db.ClimateSchedules.Add(schedule);
        await db.SaveChangesAsync(ct);
        return schedule;
    }

    public async Task<ClimateSchedule?> UpdateAsync(
        int vehicleId, int id, Action<ClimateSchedule> change, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var schedule = await db.ClimateSchedules.FirstOrDefaultAsync(s => s.Id == id && s.VehicleId == vehicleId, ct);
            if (schedule is null) return null;

            change(schedule);
            schedule.Version++;
            try
            {
                await db.SaveChangesAsync(ct);
                return schedule;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // The other process saved it (or deleted it) in between: start over from its save.
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<bool> DeleteAsync(int vehicleId, int id, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var schedule = await db.ClimateSchedules.FirstOrDefaultAsync(s => s.Id == id && s.VehicleId == vehicleId, ct);
            if (schedule is null) return false;

            db.ClimateSchedules.Remove(schedule);
            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // The Worker saved it in between; deleting what it saved is still what was asked.
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<IReadOnlyList<ClimateSchedule>> GetDueAsync(DateTime nowUtc, CancellationToken ct = default) =>
        await db.ClimateSchedules.AsNoTracking()
            .Include(s => s.Vehicle)
            .Where(s => s.Enabled && s.NextRunUtc != null && s.NextRunUtc <= nowUtc)
            .OrderBy(s => s.NextRunUtc)
            .ToListAsync(ct);

    public async Task<bool> TryClaimAsync(ClimateSchedule due, Action<ClimateSchedule> claim, CancellationToken ct = default)
    {
        // Claimed only as it was read: an edit or delete since then leaves the run to the next look.
        var schedule = await db.ClimateSchedules.FirstOrDefaultAsync(s => s.Id == due.Id, ct);
        if (schedule is null || schedule.Version != due.Version) return false;

        claim(schedule);
        schedule.Version++;
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Saved between the read above and this write.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task MarkInterruptedAsync(CancellationToken ct = default)
    {
        var interrupted = await db.ClimateSchedules.AsNoTracking()
            .Where(s => s.LastRunOutcome == ClimateScheduleOutcome.Running)
            .Select(s => new { s.Id, s.VehicleId })
            .ToListAsync(ct);

        foreach (var run in interrupted)
        {
            await UpdateAsync(run.VehicleId, run.Id, s =>
            {
                if (s.LastRunOutcome == ClimateScheduleOutcome.Running)
                    s.LastRunOutcome = ClimateScheduleOutcome.Unconfirmed;
            }, ct);
        }
    }
}
