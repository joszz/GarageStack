using GarageStack.Api;
using GarageStack.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GarageStack.Tests;

public class TokenRevocationTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static IMemoryCache CreateCache() => new MemoryCache(new MemoryCacheOptions());

    [Fact]
    public async Task IsRevokedAsync_UnknownJti_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        using var cache = CreateCache();

        Assert.False(await TokenRevocation.IsRevokedAsync(db, cache, "never-issued", ct));
    }

    [Fact]
    public async Task RevokeAsync_ThenIsRevokedAsync_ReturnsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        using var cache = CreateCache();

        await TokenRevocation.RevokeAsync(db, cache, "abc123", DateTime.UtcNow.AddHours(1), ct);

        Assert.True(await TokenRevocation.IsRevokedAsync(db, cache, "abc123", ct));
    }

    [Fact]
    public async Task RevokeAsync_DifferentJti_DoesNotAffectOtherTokens()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        using var cache = CreateCache();

        await TokenRevocation.RevokeAsync(db, cache, "revoked-token", DateTime.UtcNow.AddHours(1), ct);

        Assert.False(await TokenRevocation.IsRevokedAsync(db, cache, "still-valid-token", ct));
    }

    [Fact]
    public async Task RevokeAsync_PrunesRowsPastTheirOwnExpiry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        using var cache = CreateCache();

        // Simulate a stale revocation from a token that has since naturally expired.
        await TokenRevocation.RevokeAsync(db, cache, "long-expired", DateTime.UtcNow.AddDays(-1), ct);
        Assert.Equal(1, await db.RevokedTokens.CountAsync(ct));

        // Revoking a new, still-valid token should sweep the stale row above.
        await TokenRevocation.RevokeAsync(db, cache, "freshly-revoked", DateTime.UtcNow.AddHours(1), ct);

        Assert.False(await TokenRevocation.IsRevokedAsync(db, cache, "long-expired", ct));
        Assert.True(await TokenRevocation.IsRevokedAsync(db, cache, "freshly-revoked", ct));
        Assert.Equal(1, await db.RevokedTokens.CountAsync(ct));
    }

    [Fact]
    public async Task IsRevokedAsync_SecondCheck_IsAnsweredFromCacheWithoutTheDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        using var cache = CreateCache();

        Assert.False(await TokenRevocation.IsRevokedAsync(db, cache, "cached-session", ct));

        // A row inserted behind the cache's back is not seen until the cached answer expires,
        // which is the trade-off that removes a database round trip from every request.
        db.RevokedTokens.Add(new Core.Models.RevokedToken { Jti = "cached-session", ExpiresAtUtc = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync(ct);

        Assert.False(await TokenRevocation.IsRevokedAsync(db, cache, "cached-session", ct));
    }

    [Fact]
    public async Task RevokeAsync_IsVisibleImmediately_EvenAfterACachedNotRevokedAnswer()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        using var cache = CreateCache();

        Assert.False(await TokenRevocation.IsRevokedAsync(db, cache, "live-session", ct));

        await TokenRevocation.RevokeAsync(db, cache, "live-session", DateTime.UtcNow.AddHours(1), ct);

        Assert.True(await TokenRevocation.IsRevokedAsync(db, cache, "live-session", ct));
    }
}
