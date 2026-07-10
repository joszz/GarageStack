namespace GarageStack.Core.Models;

// Records a JWT's jti (unique ID) as revoked so logout actually invalidates the token
// server-side, instead of only removing the client-side cookie. ExpiresAtUtc mirrors the
// token's own "exp" claim so rows past their token's natural expiry can be pruned -- once a
// token has expired, JwtBearer's own ValidateLifetime already rejects it regardless of this
// table, so there's no need to keep the row around.
public class RevokedToken
{
    public long Id { get; set; }
    public string Jti { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
