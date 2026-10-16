using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

public class NotificationRepositoryTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task WasSentSinceAsync_MatchingRecentNotification_ReturnsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.AppNotifications.Add(new AppNotification
        {
            Category = "low-tyre",
            VehicleId = 1,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            Title = "Low Tyre Pressure",
            Body = "...",
        });
        await db.SaveChangesAsync(ct);

        var result = await new NotificationRepository(db).WasSentSinceAsync("low-tyre", 1, DateTime.UtcNow.AddMinutes(-10), ct);

        Assert.True(result);
    }

    [Fact]
    public async Task WasSentSinceAsync_NotificationBeforeCutoff_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.AppNotifications.Add(new AppNotification
        {
            Category = "low-tyre",
            VehicleId = 1,
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            Title = "Low Tyre Pressure",
            Body = "...",
        });
        await db.SaveChangesAsync(ct);

        var result = await new NotificationRepository(db).WasSentSinceAsync("low-tyre", 1, DateTime.UtcNow.AddHours(-1), ct);

        Assert.False(result);
    }

    [Fact]
    public async Task WasSentSinceAsync_DifferentVehicle_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.AppNotifications.Add(new AppNotification
        {
            Category = "low-tyre",
            VehicleId = 2,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            Title = "Low Tyre Pressure",
            Body = "...",
        });
        await db.SaveChangesAsync(ct);

        var result = await new NotificationRepository(db).WasSentSinceAsync("low-tyre", 1, DateTime.UtcNow.AddMinutes(-10), ct);

        Assert.False(result);
    }

    [Fact]
    public async Task WasSentSinceAsync_DifferentCategory_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.AppNotifications.Add(new AppNotification
        {
            Category = "high-tyre",
            VehicleId = 1,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            Title = "High Tyre Pressure",
            Body = "...",
        });
        await db.SaveChangesAsync(ct);

        var result = await new NotificationRepository(db).WasSentSinceAsync("low-tyre", 1, DateTime.UtcNow.AddMinutes(-10), ct);

        Assert.False(result);
    }

    [Fact]
    public async Task ArchiveAndDelete_ReportWhetherThereWasSuchANotification_AndTheBadgeFollows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var repo = new NotificationRepository(db);
        var first = new AppNotification { Title = "One", Body = "...", CreatedAt = DateTime.UtcNow.AddMinutes(-2) };
        var second = new AppNotification { Title = "Two", Body = "...", CreatedAt = DateTime.UtcNow.AddMinutes(-1) };
        await repo.AddAsync(first, ct);
        await repo.AddAsync(second, ct);

        Assert.True(await repo.ArchiveAsync(first.Id, ct));
        Assert.True(await repo.DeleteAsync(second.Id, ct));

        Assert.False(await repo.ArchiveAsync(second.Id, ct));
        Assert.False(await repo.DeleteAsync(999, ct));
        Assert.Equal(0, await repo.CountUnreadAsync(ct));
        Assert.Equal("One", Assert.Single(await repo.ListAsync(10, ct)).Title);
    }
}
