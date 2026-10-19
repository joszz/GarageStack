using GarageStack.Core.Models;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

// Archiving and deleting every notification at once are bulk updates (ExecuteUpdate), which only
// a relational database runs.
public class NotificationRepositoryPostgresTests
{
    [Fact]
    public async Task ArchiveAll_AndDeleteAll_ChangeEveryRowTheyShould()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        db.AppNotifications.AddRange(
            new AppNotification { Title = "Unread" },
            new AppNotification { Title = "Read", IsArchived = true },
            new AppNotification { Title = "Gone", IsDeleted = true });
        await db.SaveChangesAsync(ct);
        var repo = new NotificationRepository(db);

        await repo.ArchiveAllAsync(ct);
        var unreadAfterArchive = await repo.CountUnreadAsync(ct);
        await repo.DeleteAllAsync(ct);

        await using var check = database.CreateContext();
        Assert.Equal(0, unreadAfterArchive);
        Assert.Empty(await new NotificationRepository(check).ListAsync(10, ct));
        Assert.Equal(3, await check.AppNotifications.CountAsync(n => n.IsDeleted, ct));
    }

    [Fact]
    public async Task TheCooldownLookup_FindsASendForThatCarAndCategoryOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var now = DateTime.UtcNow;
        var vehicle = new Vehicle { Vin = "FAKEVN00000000001" };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);
        db.AppNotifications.Add(new AppNotification
        {
            Title = "Low tyre",
            Category = NotificationCategories.LowTyre,
            VehicleId = vehicle.Id,
            CreatedAt = now.AddMinutes(-10),
        });
        await db.SaveChangesAsync(ct);
        var repo = new NotificationRepository(db);

        Assert.True(await repo.WasSentSinceAsync(NotificationCategories.LowTyre, vehicle.Id, now.AddHours(-1), ct));
        Assert.False(await repo.WasSentSinceAsync(NotificationCategories.LowFuel, vehicle.Id, now.AddHours(-1), ct));
        Assert.False(await repo.WasSentSinceAsync(NotificationCategories.LowTyre, vehicle.Id, now.AddMinutes(-5), ct));
    }
}
