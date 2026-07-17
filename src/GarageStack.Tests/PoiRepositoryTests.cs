using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GarageStack.Tests;

public class PoiRepositoryTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static PoiRepository CreateRepo(AppDbContext db) =>
        new(db, new MemoryCache(new MemoryCacheOptions()));

    private static PoiItem MakeItem(string source, string poiType, string externalId, double lat, double lng) => new()
    {
        Source = source,
        PoiType = poiType,
        ExternalId = externalId,
        Latitude = lat,
        Longitude = lng,
        CellLat = (int)lat,
        CellLng = (int)lng,
    };

    [Fact]
    public async Task GetPoisInBoundsAsync_ReturnsOnlyItemsWithinBounds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.PoiItems.AddRange(
            MakeItem("overpass", "fuel", "inside", lat: 52.0, lng: 5.0),
            MakeItem("overpass", "fuel", "outside-lat", lat: 60.0, lng: 5.0),
            MakeItem("overpass", "fuel", "outside-lng", lat: 52.0, lng: 20.0));
        await db.SaveChangesAsync(ct);

        var result = await CreateRepo(db).GetPoisInBoundsAsync(
            "overpass", "fuel", minLat: 51.0, minLng: 4.0, maxLat: 53.0, maxLng: 6.0, ct);

        Assert.Single(result);
        Assert.Equal("inside", result[0].ExternalId);
    }

    [Fact]
    public async Task GetPoisInBoundsAsync_FiltersBySourceAndPoiType()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.PoiItems.AddRange(
            MakeItem("overpass", "fuel", "match", lat: 52.0, lng: 5.0),
            MakeItem("ocm", "charging", "wrong-source", lat: 52.0, lng: 5.0),
            MakeItem("overpass", "service_area", "wrong-type", lat: 52.0, lng: 5.0));
        await db.SaveChangesAsync(ct);

        var result = await CreateRepo(db).GetPoisInBoundsAsync(
            "overpass", "fuel", minLat: 51.0, minLng: 4.0, maxLat: 53.0, maxLng: 6.0, ct);

        Assert.Single(result);
        Assert.Equal("match", result[0].ExternalId);
    }

    [Fact]
    public async Task GetPoisInBoundsAsync_NoMatches_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();

        var result = await CreateRepo(db).GetPoisInBoundsAsync(
            "overpass", "fuel", minLat: 51.0, minLng: 4.0, maxLat: 53.0, maxLng: 6.0, ct);

        Assert.Empty(result);
    }
}
