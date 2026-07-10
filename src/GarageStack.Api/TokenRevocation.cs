using GarageStack.Core.Models;
using GarageStack.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Api;

internal static class TokenRevocation
{
    // Revokes the given token and opportunistically prunes rows past their own token's expiry --
    // once a token has expired, JwtBearer's ValidateLifetime already rejects it regardless of
    // this table, so there's no need for a dedicated background sweep.
    internal static async Task RevokeAsync(AppDbContext db, string jti, DateTime expiresAtUtc, CancellationToken ct)
    {
        db.RevokedTokens.Add(new RevokedToken { Jti = jti, ExpiresAtUtc = expiresAtUtc });

        var expired = db.RevokedTokens.Where(r => r.ExpiresAtUtc < DateTime.UtcNow);
        db.RevokedTokens.RemoveRange(expired);

        await db.SaveChangesAsync(ct);
    }

    internal static Task<bool> IsRevokedAsync(AppDbContext db, string jti, CancellationToken ct) =>
        db.RevokedTokens.AnyAsync(r => r.Jti == jti, ct);
}
