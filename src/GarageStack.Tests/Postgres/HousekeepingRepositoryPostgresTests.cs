using GarageStack.Core.Models;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

// On PostgreSQL the housekeeping deletes run as one DELETE each (ExecuteDelete), a path the
// in-memory tests cannot take.
public class HousekeepingRepositoryPostgresTests
{
    [Fact]
    public async Task ExpiredCachesAndOldDeletedNotifications_GoInOneStatementEach()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var now = DateTime.UtcNow;
        db.GeocodeCacheEntries.AddRange(
            new GeocodeCacheEntry { Precision = "street", Language = "en", CellLat = 1, CellLng = 1, ExpiresAt = now.AddDays(-2) },
            new GeocodeCacheEntry { Precision = "street", Language = "en", CellLat = 2, CellLng = 2, ExpiresAt = now.AddDays(5) });
        db.AppNotifications.AddRange(
            new AppNotification { Title = "Old, deleted", IsDeleted = true, CreatedAt = now.AddDays(-40) },
            new AppNotification { Title = "Old, kept", CreatedAt = now.AddDays(-40) },
            new AppNotification { Title = "New, deleted", IsDeleted = true, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        var repo = new HousekeepingRepository(db);

        var caches = await repo.RemoveExpiredCacheEntriesAsync(now, ct);
        var notifications = await repo.RemoveDeletedNotificationsAsync(now.AddDays(-30), ct);

        await using var check = database.CreateContext();
        Assert.Equal((1, 1), (caches, notifications));
        Assert.Equal(1, await check.GeocodeCacheEntries.CountAsync(ct));
        Assert.Equal(["New, deleted", "Old, kept"], await check.AppNotifications.OrderBy(n => n.Title).Select(n => n.Title).ToListAsync(ct));
    }
}
