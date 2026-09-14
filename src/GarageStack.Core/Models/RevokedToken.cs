namespace GarageStack.Core.Models;

// Records a session id as revoked so logout actually invalidates the session server-side,
// instead of only removing the client-side cookie. ExpiresAtUtc mirrors the session's own
// expiry so rows past it can be pruned -- once a session has expired the cookie handler
// rejects it regardless of this table, so there's no need to keep the row around.
public class RevokedToken
{
    public long Id { get; set; }
    public string Jti { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
