using GarageStack.Api;
using GarageStack.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

public class TokenRevocationTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task IsRevokedAsync_UnknownJti_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();

        Assert.False(await TokenRevocation.IsRevokedAsync(db, "never-issued", ct));
    }

    [Fact]
    public async Task RevokeAsync_ThenIsRevokedAsync_ReturnsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();

        await TokenRevocation.RevokeAsync(db, "abc123", DateTime.UtcNow.AddHours(1), ct);

        Assert.True(await TokenRevocation.IsRevokedAsync(db, "abc123", ct));
    }

    [Fact]
    public async Task RevokeAsync_DifferentJti_DoesNotAffectOtherTokens()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();

        await TokenRevocation.RevokeAsync(db, "revoked-token", DateTime.UtcNow.AddHours(1), ct);

        Assert.False(await TokenRevocation.IsRevokedAsync(db, "still-valid-token", ct));
    }

    [Fact]
    public async Task RevokeAsync_PrunesRowsPastTheirOwnExpiry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();

        // Simulate a stale revocation from a token that has since naturally expired.
        await TokenRevocation.RevokeAsync(db, "long-expired", DateTime.UtcNow.AddDays(-1), ct);
        Assert.Equal(1, await db.RevokedTokens.CountAsync(ct));

        // Revoking a new, still-valid token should sweep the stale row above.
        await TokenRevocation.RevokeAsync(db, "freshly-revoked", DateTime.UtcNow.AddHours(1), ct);

        Assert.False(await TokenRevocation.IsRevokedAsync(db, "long-expired", ct));
        Assert.True(await TokenRevocation.IsRevokedAsync(db, "freshly-revoked", ct));
        Assert.Equal(1, await db.RevokedTokens.CountAsync(ct));
    }
}
