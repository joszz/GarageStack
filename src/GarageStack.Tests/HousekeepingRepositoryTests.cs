using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

public class HousekeepingRepositoryTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task ExpiredPlaceNamesAndSnappedTrips_AreRemoved_AndValidOnesKept()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.GeocodeCacheEntries.AddRange(
            new GeocodeCacheEntry { Precision = "city", Language = "en", CellLat = 1, City = "Expired", ExpiresAt = Now.AddDays(-2) },
            new GeocodeCacheEntry { Precision = "city", Language = "en", CellLat = 2, City = "Valid", ExpiresAt = Now.AddDays(1) });
        db.MapMatchCacheEntries.AddRange(
            new MapMatchCacheEntry { Provider = "valhalla", TraceHash = "expired", ExpiresAt = Now.AddDays(-2) },
            new MapMatchCacheEntry { Provider = "valhalla", TraceHash = "valid", ExpiresAt = Now.AddDays(1) });
        await db.SaveChangesAsync(ct);

        Assert.Equal(2, await new HousekeepingRepository(db).RemoveExpiredCacheEntriesAsync(Now.AddDays(-1), ct));

        Assert.Equal(["Valid"], await db.GeocodeCacheEntries.Select(e => e.City).ToListAsync(ct));
        Assert.Equal(["valid"], await db.MapMatchCacheEntries.Select(e => e.TraceHash).ToListAsync(ct));
    }

    [Fact]
    public async Task DeletedNotifications_AreRemovedOnceOldEnough_AndArchivedOnesKept()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.AppNotifications.AddRange(
            new AppNotification { Title = "deleted long ago", IsDeleted = true, CreatedAt = Now.AddDays(-40) },
            new AppNotification { Title = "deleted recently", IsDeleted = true, CreatedAt = Now.AddDays(-5) },
            new AppNotification { Title = "archived long ago", IsArchived = true, CreatedAt = Now.AddDays(-40) },
            new AppNotification { Title = "unread long ago", CreatedAt = Now.AddDays(-40) });
        await db.SaveChangesAsync(ct);

        Assert.Equal(1, await new HousekeepingRepository(db).RemoveDeletedNotificationsAsync(Now.AddDays(-30), ct));

        Assert.Equal(
            ["archived long ago", "deleted recently", "unread long ago"],
            await db.AppNotifications.OrderBy(n => n.Title).Select(n => n.Title).ToListAsync(ct));
    }
}
