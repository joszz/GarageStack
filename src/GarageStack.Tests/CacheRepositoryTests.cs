using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GarageStack.Tests;

// The geocoder and the map matcher are tested against fakes of these; this is the real storage.
public class GeocodeRepositoryTests
{
    private static readonly PlaceAddress Damrak = new("Damrak 1, Amsterdam", "Damrak", "1", "Amsterdam", "1012 LG", "nl");

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task ReturnsExactlyTheCellsAskedFor_NotTheCornersOfTheirCrossProduct()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var repo = new GeocodeRepository(db);
        foreach (var cell in new[] { (1, 1), (1, 2), (2, 1), (2, 2) })
            await repo.UpsertAsync("street", "en", cell.Item1, cell.Item2, Damrak, TimeSpan.FromDays(1), ct);

        var found = await repo.GetValidAsync("street", "en", [(1, 1), (2, 2)], ct);

        Assert.Equal([(1, 1), (2, 2)], found.Select(e => (e.CellLat, e.CellLng)).OrderBy(c => c));
    }

    [Fact]
    public async Task LeavesOutExpiredEntriesAndOtherLanguages()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var repo = new GeocodeRepository(db);
        await repo.UpsertAsync("street", "en", 1, 1, Damrak, TimeSpan.FromDays(-1), ct);
        await repo.UpsertAsync("street", "nl", 2, 2, Damrak, TimeSpan.FromDays(1), ct);

        Assert.Empty(await repo.GetValidAsync("street", "en", [(1, 1), (2, 2)], ct));
    }

    [Fact]
    public async Task AnUpsert_RefreshesTheCellsRowInsteadOfAddingOne()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var repo = new GeocodeRepository(db);

        await repo.UpsertAsync("street", "en", 1, 1, Damrak, TimeSpan.FromDays(-1), ct);
        await repo.UpsertAsync("street", "en", 1, 1, Damrak with { Road = "Rokin" }, TimeSpan.FromDays(1), ct);

        var row = Assert.Single(await db.GeocodeCacheEntries.ToListAsync(ct));
        Assert.Equal("Rokin", row.Road);
        Assert.Single(await repo.GetValidAsync("street", "en", [(1, 1)], ct));
    }
}

public class MapMatchRepositoryTests
{
    private static readonly CachedTraceMatch Matched = new("encoded-polyline", [0, 2, 5], 12.5, [50, 3, 80, 2]);

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task AMatch_ComesBackAsItWasStored()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var repo = new MapMatchRepository(db);

        await repo.UpsertAsync("valhalla", "hash", Matched, TimeSpan.FromDays(1), ct);
        var cached = await repo.GetValidAsync("valhalla", "hash", ct);

        Assert.NotNull(cached);
        Assert.Equal(Matched.Shape, cached.Shape);
        Assert.Equal(Matched.PointIndexes, cached.PointIndexes);
        Assert.Equal(Matched.SpeedLimitRuns, cached.SpeedLimitRuns);
        Assert.Equal(12.5, cached.MatchedKm);
    }

    [Fact]
    public async Task ATraceThatCouldNotBeMatched_IsRememberedAsSuch()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var repo = new MapMatchRepository(db);

        await repo.UpsertAsync("valhalla", "hash", CachedTraceMatch.NotMatched, TimeSpan.FromDays(1), ct);

        Assert.Same(CachedTraceMatch.NotMatched, await repo.GetValidAsync("valhalla", "hash", ct));
    }

    [Fact]
    public async Task AnExpiredMatch_IsAMiss()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var repo = new MapMatchRepository(db);

        await repo.UpsertAsync("valhalla", "hash", Matched, TimeSpan.FromDays(-1), ct);

        Assert.Null(await repo.GetValidAsync("valhalla", "hash", ct));
    }

    // A row with unreadable point indexes cannot be drawn; one with unreadable limits still can.
    [Theory]
    [InlineData("not json", "[50,3]", false)]
    [InlineData("[0,2,5]", "not json", true)]
    public async Task AnUnreadableRow_IsAMissOnlyWhenTheLineCannotBeDrawn(string indexes, string limits, bool served)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.MapMatchCacheEntries.Add(new MapMatchCacheEntry
        {
            Provider = "valhalla",
            TraceHash = "hash",
            Shape = "encoded-polyline",
            PointIndexesJson = indexes,
            SpeedLimitRunsJson = limits,
            CachedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
        });
        await db.SaveChangesAsync(ct);
        var logger = new RecordingLogger<MapMatchRepository>();

        var cached = await new MapMatchRepository(db, logger).GetValidAsync("valhalla", "hash", ct);

        Assert.Equal(served, cached is not null);
        if (cached is not null) Assert.Empty(cached.SpeedLimitRuns);
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
    }
}
