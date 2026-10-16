using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using GarageStack.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

// One pass of the Worker's housekeeping, over the real repositories: what goes and, as much,
// what is given time to stay.
public class HousekeepingServiceTests
{
    private sealed class OnePass(IServiceScopeFactory scopes) : HousekeepingService(
        NullLogger<HousekeepingService>.Instance, scopes, new TelemetryRetentionOptions())
    {
        public Task RunAsync(CancellationToken ct) => RunOnceAsync(ct);
    }

    private static ServiceProvider Services()
    {
        var root = new InMemoryDatabaseRoot();
        var name = Guid.NewGuid().ToString();
        return new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name, root))
            .AddScoped<IHousekeepingRepository, HousekeepingRepository>()
            .AddScoped<IVehicleRepository, VehicleRepository>()
            .AddScoped<ITelemetryRepository>(sp => new TelemetryRepository(sp.GetRequiredService<AppDbContext>()))
            .AddScoped<ITripRepository>(sp => new TripRepository(sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<ITelemetryRepository>()))
            .BuildServiceProvider();
    }

    private static GeocodeCacheEntry Place(int cell, TimeSpan expiredAgo) => new()
    {
        Precision = "street",
        Language = "en",
        CellLat = cell,
        CellLng = cell,
        ExpiresAt = DateTime.UtcNow - expiredAgo,
    };

    private static AppNotification Notification(string title, bool deleted, TimeSpan age) => new()
    {
        Title = title,
        Body = "",
        IsDeleted = deleted,
        CreatedAt = DateTime.UtcNow - age,
    };

    [Fact]
    public async Task RemovesWhatIsLongGone_AndKeepsWhatMayStillBeInUse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var services = Services();
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.GeocodeCacheEntries.AddRange(
                Place(1, TimeSpan.FromDays(2)),
                // Expired an hour ago: a lookup may be refreshing it right now.
                Place(2, TimeSpan.FromHours(1)));
            db.AppNotifications.AddRange(
                Notification("deleted long ago", deleted: true, TimeSpan.FromDays(40)),
                // The notification cooldowns still read deleted rows of the last week.
                Notification("deleted lately", deleted: true, TimeSpan.FromDays(10)),
                Notification("kept by the user", deleted: false, TimeSpan.FromDays(40)));
            await db.SaveChangesAsync(ct);
        }

        await new OnePass(services.GetRequiredService<IServiceScopeFactory>()).RunAsync(ct);

        await using var check = services.CreateAsyncScope();
        var after = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal([2], await after.GeocodeCacheEntries.Select(e => e.CellLat).ToListAsync(ct));
        Assert.Equal(
            ["deleted lately", "kept by the user"],
            await after.AppNotifications.Select(n => n.Title).OrderBy(t => t).ToListAsync(ct));
    }
}
