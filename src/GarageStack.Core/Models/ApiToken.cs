namespace GarageStack.Core.Models;

/// <summary>
/// A read-only API token: a script, a dashboard or a watch reads GarageStack's data with it, as the
/// account that made it, without signing in. Only its hash is kept; the token itself is shown once.
/// </summary>
public class ApiToken
{
    public int Id { get; set; }

    /// <summary>The account the token reads as (a SHA-256 hash, see the API's SessionPrincipal).</summary>
    public string AccountKey { get; set; } = string.Empty;

    /// <summary>What the person who made it called it, to know which one to revoke.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256 of the token, in hex: what a presented token is looked up by.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>The token's first characters, so a list can tell tokens apart without showing them.</summary>
    public string Prefix { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When it was last used, to the minute: a token nobody uses any more can go.</summary>
    public DateTime? LastUsedAt { get; set; }
}

public static class ApiTokenLimits
{
    public const int NameMaxLength = 100;

    /// <summary>Enough for every script and gadget a person has; a bound so an account cannot add rows without end.</summary>
    public const int MaxPerAccount = 20;

    /// <summary>"gs_" and six characters of the token.</summary>
    public const int PrefixLength = 9;

    /// <summary>A SHA-256 hash in hex.</summary>
    public const int HashLength = 64;
}
