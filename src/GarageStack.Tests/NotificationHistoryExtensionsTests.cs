using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

public class NotificationHistoryExtensionsTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task WasNotificationSentSinceAsync_MatchingRecentNotification_ReturnsTrue()
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

        var result = await db.WasNotificationSentSinceAsync("low-tyre", 1, DateTime.UtcNow.AddMinutes(-10), ct);

        Assert.True(result);
    }

    [Fact]
    public async Task WasNotificationSentSinceAsync_NotificationBeforeCutoff_ReturnsFalse()
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

        var result = await db.WasNotificationSentSinceAsync("low-tyre", 1, DateTime.UtcNow.AddHours(-1), ct);

        Assert.False(result);
    }

    [Fact]
    public async Task WasNotificationSentSinceAsync_DifferentVehicle_ReturnsFalse()
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

        var result = await db.WasNotificationSentSinceAsync("low-tyre", 1, DateTime.UtcNow.AddMinutes(-10), ct);

        Assert.False(result);
    }

    [Fact]
    public async Task WasNotificationSentSinceAsync_DifferentCategory_ReturnsFalse()
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

        var result = await db.WasNotificationSentSinceAsync("low-tyre", 1, DateTime.UtcNow.AddMinutes(-10), ct);

        Assert.False(result);
    }
}
