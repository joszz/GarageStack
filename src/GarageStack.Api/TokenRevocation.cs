using GarageStack.Core.Models;
using GarageStack.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GarageStack.Api;

/// <summary>
/// Server-side session revocation, checked on every authenticated request by the cookie
/// handler. The answer is cached in memory so that check does not cost a database round trip
/// per request: a revocation made by this process is written to the cache immediately, and a
/// "not revoked" answer is only trusted for a short while. GarageStack runs as a single Api
/// instance (see ARCHITECTURE.md), so nothing else can revoke a session behind the cache's back.
/// </summary>
internal static class TokenRevocation
{
    private static readonly TimeSpan NotRevokedCacheTtl = TimeSpan.FromMinutes(5);

    private static string CacheKey(string jti) => $"session-revoked/{jti}";

    // Revokes the given session and opportunistically prunes rows past their own session's
    // expiry -- once a session has expired the cookie handler rejects it regardless of this
    // table, so there's no need for a dedicated background sweep.
    internal static async Task RevokeAsync(AppDbContext db, IMemoryCache cache, string jti, DateTime expiresAtUtc, CancellationToken ct)
    {
        db.RevokedTokens.Add(new RevokedToken { Jti = jti, ExpiresAtUtc = expiresAtUtc });
        await PruneExpiredAsync(db, ct);
        await db.SaveChangesAsync(ct);

        // Remember the revocation for as long as the cookie itself could still be presented.
        var remaining = expiresAtUtc - DateTime.UtcNow;
        if (remaining > TimeSpan.Zero)
            cache.Set(CacheKey(jti), true, remaining);
        else
            cache.Remove(CacheKey(jti));
    }

    internal static async Task<bool> IsRevokedAsync(AppDbContext db, IMemoryCache cache, string jti, CancellationToken ct)
    {
        var key = CacheKey(jti);
        if (cache.TryGetValue(key, out bool cached))
            return cached;

        var revoked = await db.RevokedTokens.AnyAsync(r => r.Jti == jti, ct);
        cache.Set(key, revoked, NotRevokedCacheTtl);
        return revoked;
    }

    private static async Task PruneExpiredAsync(AppDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expired = db.RevokedTokens.Where(r => r.ExpiresAtUtc < now);

        // A single DELETE on PostgreSQL; the in-memory provider used by the tests cannot run
        // bulk operations, so it falls back to loading the (tiny) set into the change tracker.
        if (db.Database.IsRelational())
            await expired.ExecuteDeleteAsync(ct);
        else
            db.RevokedTokens.RemoveRange(expired);
    }
}
