using System.Security.Claims;

namespace GarageStack.Api.Authentication;

/// <param name="Allowed">Whether the authenticated user may use GarageStack.</param>
/// <param name="Reason">
/// Written to the log, so it never contains the email address itself: the log line already names
/// the account, and log files are no place for personal data.
/// </param>
internal readonly record struct OidcAccessDecision(bool Allowed, string Reason);

/// <summary>
/// Decides whether a user who successfully authenticated at the identity provider may use
/// GarageStack. Without an allow-list every account the provider accepts gets in -- fine when
/// the provider itself restricts the application, dangerous with a public provider (any Google
/// account, for instance) on an app that can unlock a car.
/// </summary>
internal static class OidcAccessPolicy
{
    private const string EmailClaim = "email";
    private const string EmailVerifiedClaim = "email_verified";

    internal static OidcAccessDecision Evaluate(ClaimsPrincipal principal, OidcOptions options)
    {
        var allowedGroups = options.AllowedGroupList;
        var allowedEmails = options.AllowedEmailList;

        if (allowedGroups.Count == 0 && allowedEmails.Count == 0)
            return new OidcAccessDecision(true, "no access restrictions configured");

        if (allowedGroups.Count > 0)
        {
            var match = principal.FindAll(options.GroupsClaim)
                .Select(c => c.Value)
                .FirstOrDefault(g => allowedGroups.Contains(g, StringComparer.OrdinalIgnoreCase));

            if (match is not null)
                return new OidcAccessDecision(true, $"member of allowed group '{match}'");
        }

        if (allowedEmails.Count > 0)
        {
            var email = FindEmail(principal);
            if (!string.IsNullOrWhiteSpace(email) && allowedEmails.Contains(email, StringComparer.OrdinalIgnoreCase))
            {
                // An allow-list keyed on an address the provider itself has not verified is worth
                // no more than the provider's signup form.
                return IsEmailVerified(principal)
                    ? new OidcAccessDecision(true, "email is allow-listed")
                    : new OidcAccessDecision(false, "email is allow-listed but the provider reports it as unverified");
            }
        }

        return new OidcAccessDecision(false, BuildDenialReason(principal, options));
    }

    private static string BuildDenialReason(ClaimsPrincipal principal, OidcOptions options)
    {
        var parts = new List<string>(2);

        if (options.AllowedGroupList.Count > 0)
        {
            var userGroups = principal.FindAll(options.GroupsClaim).Select(c => c.Value).ToArray();
            parts.Add(userGroups.Length == 0
                ? $"no '{options.GroupsClaim}' claim was returned by the provider (OIDC_ALLOWED_GROUPS requires one)"
                : $"groups [{string.Join(", ", userGroups)}] do not match OIDC_ALLOWED_GROUPS");
        }

        if (options.AllowedEmailList.Count > 0)
        {
            parts.Add(string.IsNullOrWhiteSpace(FindEmail(principal))
                ? "no 'email' claim was returned by the provider (OIDC_ALLOWED_EMAILS requires one)"
                : "email is not in OIDC_ALLOWED_EMAILS");
        }

        return string.Join("; ", parts);
    }

    private static string? FindEmail(ClaimsPrincipal principal) =>
        principal.FindFirst(EmailClaim)?.Value ?? principal.FindFirst(ClaimTypes.Email)?.Value;

    // Providers that omit email_verified entirely (common for self-hosted ones, where accounts
    // are created by an administrator) are taken at their word.
    private static bool IsEmailVerified(ClaimsPrincipal principal)
    {
        var claim = principal.FindFirst(EmailVerifiedClaim)?.Value;
        return string.IsNullOrWhiteSpace(claim) || !bool.TryParse(claim, out var verified) || verified;
    }
}
