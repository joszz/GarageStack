using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

// The Api edits a schedule while the Worker claims its run: the version column decides, so a run
// is never claimed from a schedule that changed since it was read, and an edit is never lost.
public class ClimateScheduleRepositoryPostgresTests
{
    private static async Task<ClimateSchedule> AddScheduleAsync(AppDbContext db, CancellationToken ct)
    {
        var vehicle = new Vehicle { Vin = "FAKEVN00000000001" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);
        return await new ClimateScheduleRepository(db).AddAsync(new ClimateSchedule
        {
            VehicleId = vehicle.Id,
            StartTime = new TimeOnly(7, 30),
            TimeZoneId = "Europe/Amsterdam",
            NextRunUtc = DateTime.UtcNow.AddMinutes(-1),
        }, ct);
    }

    [Fact]
    public async Task AClaim_OnAScheduleEditedSinceItWasRead_IsLeftForTheNextLook()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var schedule = await AddScheduleAsync(db, ct);

        await using var worker = database.CreateContext();
        var due = Assert.Single(await new ClimateScheduleRepository(worker).GetDueAsync(DateTime.UtcNow, ct));
        await using (var api = database.CreateContext())
            await new ClimateScheduleRepository(api).UpdateAsync(schedule.VehicleId, schedule.Id, s => s.TemperatureC = 23, ct);

        var claimed = await new ClimateScheduleRepository(worker).TryClaimAsync(due, s => s.NextRunUtc = null, ct);

        await using var check = database.CreateContext();
        var saved = await check.ClimateSchedules.SingleAsync(ct);
        Assert.False(claimed);
        Assert.Equal(23, saved.TemperatureC);
        Assert.NotNull(saved.NextRunUtc);
    }

    [Fact]
    public async Task AnEdit_RacingAClaim_IsAppliedOnTopOfIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var schedule = await AddScheduleAsync(db, ct);

        // The Api has the schedule loaded; the Worker claims the run before the Api saves its edit.
        await using var api = database.CreateContext();
        var stale = await api.ClimateSchedules.SingleAsync(ct);
        await using (var worker = database.CreateContext())
        {
            var due = Assert.Single(await new ClimateScheduleRepository(worker).GetDueAsync(DateTime.UtcNow, ct));
            Assert.True(await new ClimateScheduleRepository(worker).TryClaimAsync(due, s => s.LastRunOutcome = ClimateScheduleOutcome.Running, ct));
        }
        stale.TemperatureC = 25;
        stale.Version++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => api.SaveChangesAsync(ct));
        api.ChangeTracker.Clear();

        await new ClimateScheduleRepository(api).UpdateAsync(schedule.VehicleId, schedule.Id, s => s.TemperatureC = 25, ct);

        await using var check = database.CreateContext();
        var saved = await check.ClimateSchedules.SingleAsync(ct);
        Assert.Equal((25, ClimateScheduleOutcome.Running), (saved.TemperatureC, saved.LastRunOutcome));
    }
}
