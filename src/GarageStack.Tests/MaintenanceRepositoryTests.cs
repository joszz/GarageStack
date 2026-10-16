using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

public class MaintenanceRepositoryTests
{
    private static readonly DateTime March = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime June = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<(MaintenanceRepository Repo, MaintenanceItem Item)> SetupAsync(CancellationToken ct)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.Vehicles.AddRange(
            new Vehicle { Id = 1, Vin = "FAKEVN00000000001" },
            new Vehicle { Id = 2, Vin = "FAKEVN00000000002" });
        await db.SaveChangesAsync(ct);
        var repo = new MaintenanceRepository(db);
        var item = await repo.AddAsync(new MaintenanceItem { VehicleId = 1, Name = "Oil", IntervalMonths = 12 }, ct);
        return (repo, item);
    }

    [Fact]
    public async Task LogService_MovesTheBaselineToTheLatestService_NotTheLastLogged()
    {
        var ct = TestContext.Current.CancellationToken;
        var (repo, item) = await SetupAsync(ct);

        await repo.LogServiceAsync(1, item.Id, new MaintenanceLogEntry { PerformedAt = June, OdometerKm = 20_000 }, ct);
        var logged = await repo.LogServiceAsync(1, item.Id, new MaintenanceLogEntry { PerformedAt = March, OdometerKm = 15_000 }, ct);

        Assert.NotNull(logged);
        Assert.Equal(June, logged.Item.LastServiceDate);
        Assert.Equal(20_000, logged.Item.LastServiceOdometerKm);
        Assert.Equal(item.Id, logged.Entry.MaintenanceItemId);
    }

    [Fact]
    public async Task DeleteLogEntry_FallsBackToTheServiceBefore_AndToNoneWhenTheHistoryEmpties()
    {
        var ct = TestContext.Current.CancellationToken;
        var (repo, item) = await SetupAsync(ct);
        var march = await repo.LogServiceAsync(1, item.Id, new MaintenanceLogEntry { PerformedAt = March }, ct);
        var june = await repo.LogServiceAsync(1, item.Id, new MaintenanceLogEntry { PerformedAt = June }, ct);

        var afterJune = await repo.DeleteLogEntryAsync(1, item.Id, june!.Entry.Id, ct);
        Assert.Equal(March, afterJune!.LastServiceDate);

        var afterMarch = await repo.DeleteLogEntryAsync(1, item.Id, march!.Entry.Id, ct);
        Assert.Null(afterMarch!.LastServiceDate);
        Assert.Empty((await repo.GetLogAsync(1, item.Id, ct))!);
    }

    [Fact]
    public async Task AnotherVehiclesItem_IsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var (repo, item) = await SetupAsync(ct);

        Assert.Null(await repo.GetLogAsync(2, item.Id, ct));
        Assert.Null(await repo.UpdateAsync(2, item.Id, i => i.Name = "Stolen", ct));
        Assert.Null(await repo.LogServiceAsync(2, item.Id, new MaintenanceLogEntry { PerformedAt = June }, ct));
        Assert.False(await repo.DeleteAsync(2, item.Id, ct));
        Assert.Equal("Oil", Assert.Single(await repo.ListAsync(1, ct)).Name);
    }
}
