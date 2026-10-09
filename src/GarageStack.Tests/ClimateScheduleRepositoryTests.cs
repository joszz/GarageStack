using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GarageStack.Tests;

public class ClimateScheduleRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 5, 30, 0, DateTimeKind.Utc);

    // Every context of a test shares this root, so the Api's and the Worker's contexts see one database.
    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _name = Guid.NewGuid().ToString();

    private AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_name, _root).Options);

    private async Task<(int VehicleId, int ScheduleId)> SeedAsync(Action<ClimateSchedule>? configure = null)
    {
        await using var db = NewDb();
        var vehicle = new Vehicle { Vin = "FAKEVN00000000001", SaicUser = "user" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var schedule = new ClimateSchedule
        {
            VehicleId = vehicle.Id,
            StartTime = new TimeOnly(7, 30),
            Days = ClimateScheduleDays.Friday,
            TimeZoneId = "Europe/Amsterdam",
            NextRunUtc = Now,
        };
        configure?.Invoke(schedule);
        db.ClimateSchedules.Add(schedule);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (vehicle.Id, schedule.Id);
    }

    private async Task<ClimateSchedule> ReadAsync(int id)
    {
        await using var db = NewDb();
        return await db.ClimateSchedules.AsNoTracking().SingleAsync(s => s.Id == id, TestContext.Current.CancellationToken);
    }

    // Saves the schedule from a second context, as the other process would.
    private void SaveElsewhere(int id, Action<ClimateSchedule> change)
    {
        using var db = NewDb();
        var schedule = db.ClimateSchedules.Single(s => s.Id == id);
        change(schedule);
        schedule.Version++;
        db.SaveChanges();
    }

    [Fact]
    public async Task UpdateAsync_BeatenByTheOtherProcess_AppliesTheChangeAgainOnTopOfItsSave()
    {
        var ct = TestContext.Current.CancellationToken;
        var (vehicleId, id) = await SeedAsync();
        await using var db = NewDb();
        var repo = new ClimateScheduleRepository(db);
        var attempts = 0;

        var updated = await repo.UpdateAsync(vehicleId, id, s =>
        {
            if (++attempts == 1)
                SaveElsewhere(id, other => other.LastRunOutcome = ClimateScheduleOutcome.Started);
            s.Name = "Work";
        }, ct);

        var saved = await ReadAsync(id);
        Assert.Equal(2, attempts);
        Assert.NotNull(updated);
        Assert.Equal("Work", saved.Name);
        Assert.Equal(ClimateScheduleOutcome.Started, saved.LastRunOutcome);
    }

    [Fact]
    public async Task UpdateAsync_AnotherVehiclesSchedule_IsNotFound()
    {
        var (vehicleId, id) = await SeedAsync();
        await using var db = NewDb();

        var updated = await new ClimateScheduleRepository(db).UpdateAsync(vehicleId + 1, id, s => s.Name = "x", TestContext.Current.CancellationToken);

        Assert.Null(updated);
    }

    [Fact]
    public async Task GetDueAsync_ReturnsOnlySwitchedOnSchedulesThatAreDue_WithTheirVehicle()
    {
        var ct = TestContext.Current.CancellationToken;
        var (vehicleId, dueId) = await SeedAsync();
        await using (var db = NewDb())
        {
            db.ClimateSchedules.AddRange(
                new ClimateSchedule { VehicleId = vehicleId, TimeZoneId = "UTC", NextRunUtc = Now.AddMinutes(1) },
                new ClimateSchedule { VehicleId = vehicleId, TimeZoneId = "UTC", NextRunUtc = Now, Enabled = false });
            await db.SaveChangesAsync(ct);
        }

        await using var read = NewDb();
        var due = await new ClimateScheduleRepository(read).GetDueAsync(Now, ct);

        var schedule = Assert.Single(due);
        Assert.Equal(dueId, schedule.Id);
        Assert.Equal("FAKEVN00000000001", schedule.Vehicle.Vin);
    }

    [Fact]
    public async Task TryClaimAsync_AsRead_SavesTheClaim()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync();
        await using var db = NewDb();
        var repo = new ClimateScheduleRepository(db);
        var due = Assert.Single(await repo.GetDueAsync(Now, ct));

        var claimed = await repo.TryClaimAsync(due, s => s.LastRunOutcome = ClimateScheduleOutcome.Running, ct);

        Assert.True(claimed);
        var saved = await ReadAsync(due.Id);
        Assert.Equal(ClimateScheduleOutcome.Running, saved.LastRunOutcome);
        Assert.Equal(due.Version + 1, saved.Version);
    }

    [Fact]
    public async Task TryClaimAsync_EditedSinceItWasRead_LeavesItAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync();
        await using var db = NewDb();
        var repo = new ClimateScheduleRepository(db);
        var due = Assert.Single(await repo.GetDueAsync(Now, ct));
        SaveElsewhere(due.Id, s => s.Enabled = false);

        var claimed = await repo.TryClaimAsync(due, s => s.LastRunOutcome = ClimateScheduleOutcome.Running, ct);

        Assert.False(claimed);
        Assert.Null((await ReadAsync(due.Id)).LastRunOutcome);
    }

    [Fact]
    public async Task MarkInterruptedAsync_TurnsRunningRunsIntoUnconfirmedOnes()
    {
        var (_, running) = await SeedAsync(s => s.LastRunOutcome = ClimateScheduleOutcome.Running);
        await using var db = NewDb();

        await new ClimateScheduleRepository(db).MarkInterruptedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ClimateScheduleOutcome.Unconfirmed, (await ReadAsync(running)).LastRunOutcome);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyTheVehiclesOwnSchedule()
    {
        var ct = TestContext.Current.CancellationToken;
        var (vehicleId, id) = await SeedAsync();
        await using var db = NewDb();
        var repo = new ClimateScheduleRepository(db);

        Assert.False(await repo.DeleteAsync(vehicleId + 1, id, ct));
        Assert.True(await repo.DeleteAsync(vehicleId, id, ct));
        Assert.Equal(0, await repo.CountAsync(vehicleId, ct));
    }
}
