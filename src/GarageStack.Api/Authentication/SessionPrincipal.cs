using System.Security.Claims;

namespace GarageStack.Api.Authentication;

/// <summary>
/// Builds the identity that ends up inside the auth cookie. Identity providers hand back
/// anything from a handful of claims to a full group membership list; GarageStack only needs a
/// display name, the subject, and a session id, and every extra claim is carried on every
/// single request because the cookie travels with them.
/// </summary>
internal static class SessionPrincipal
{
    /// <summary>Identifies this sign-in so logout can revoke it server-side.</summary>
    internal const string SessionIdClaimType = "garagestack:sid";

    internal const string SubjectClaimType = "sub";

    // Ordered from most to least human-friendly: providers differ in which of these they send.
    private static readonly string[] DisplayNameClaimTypes =
    [
        "preferred_username",
        "name",
        ClaimTypes.Name,
        "email",
        ClaimTypes.Email,
        SubjectClaimType,
        ClaimTypes.NameIdentifier,
    ];

    internal static ClaimsPrincipal Create(string displayName, string? subject, string authenticationType, string sessionId)
    {
        var identity = new ClaimsIdentity(authenticationType, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.Name, displayName));
        identity.AddClaim(new Claim(SessionIdClaimType, sessionId));

        if (!string.IsNullOrWhiteSpace(subject))
            identity.AddClaim(new Claim(SubjectClaimType, subject));

        return new ClaimsPrincipal(identity);
    }

    internal static string ResolveDisplayName(ClaimsPrincipal principal)
    {
        foreach (var claimType in DisplayNameClaimTypes)
        {
            var value = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return "user";
    }

    internal static string? ResolveSubject(ClaimsPrincipal principal) =>
        principal.FindFirst(SubjectClaimType)?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    internal static string NewSessionId() => Guid.NewGuid().ToString("N");
}
