namespace GarageStack.Api.Authentication;

/// <summary>
/// Bound from the "Oidc" configuration section, which docker-compose, the all-in-one
/// entrypoint and the Unraid template fill from OIDC_* environment variables.
/// OIDC is considered configured as soon as <see cref="Authority"/> is set; everything else
/// either has a usable default or is optional.
/// </summary>
public sealed class OidcOptions
{
    public const string SectionName = "Oidc";

    /// <summary>
    /// Path the identity provider redirects back to after authentication. Deliberately under
    /// /api so the bundled nginx proxies it to the API without any extra location block.
    /// </summary>
    public const string CallbackPath = "/api/auth/oidc/callback";

    private static readonly char[] ListSeparators = [',', ' ', ';'];

    /// <summary>Issuer URL of the provider, e.g. https://auth.example.com/application/o/garagestack/.</summary>
    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    /// <summary>Optional: leave empty for a public client (PKCE only).</summary>
    public string? ClientSecret { get; set; }

    public string Scopes { get; set; } = "openid profile email";

    /// <summary>Shown on the login button ("Sign in with ...").</summary>
    public string ProviderName { get; set; } = "SSO";

    /// <summary>Send users straight to the provider instead of showing the login page.</summary>
    public bool AutoLogin { get; set; }

    /// <summary>Set to false only for a provider served over plain HTTP on the LAN.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Explicit redirect URI, for deployments where the API cannot derive its own public URL
    /// from the request (multiple reverse proxies, non-standard ports without forwarded headers).
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>Claim carrying the user's groups. Provider-specific; "groups" covers most.</summary>
    public string GroupsClaim { get; set; } = "groups";

    /// <summary>Comma-separated group allow-list. Empty means "any user the provider lets in".</summary>
    public string? AllowedGroups { get; set; }

    /// <summary>Comma-separated email allow-list. Empty means "any user the provider lets in".</summary>
    public string? AllowedEmails { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(Authority);

    public IReadOnlyList<string> ScopeList => SplitList(Scopes);

    public IReadOnlyList<string> AllowedGroupList => SplitList(AllowedGroups);

    public IReadOnlyList<string> AllowedEmailList => SplitList(AllowedEmails);

    public bool HasAccessRestrictions => AllowedGroupList.Count > 0 || AllowedEmailList.Count > 0;

    /// <summary>
    /// Fails fast on a half-finished configuration instead of letting the first login attempt
    /// die with a protocol-level error that says nothing about which variable is missing.
    /// </summary>
    public void Validate()
    {
        if (!Enabled)
        {
            if (!string.IsNullOrWhiteSpace(ClientId) || !string.IsNullOrWhiteSpace(ClientSecret))
                throw new InvalidOperationException(
                    "OIDC is half-configured: OIDC_CLIENT_ID/OIDC_CLIENT_SECRET is set but OIDC_AUTHORITY is not. " +
                    "Set OIDC_AUTHORITY to your provider's issuer URL, or clear the other OIDC_* variables.");

            return;
        }

        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var authorityUri) ||
            (authorityUri.Scheme != Uri.UriSchemeHttp && authorityUri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(
                $"OIDC_AUTHORITY must be an absolute http(s) URL, got '{Authority}'.");

        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException(
                "OIDC_CLIENT_ID is required when OIDC_AUTHORITY is set.");

        if (!string.IsNullOrWhiteSpace(RedirectUri))
        {
            if (!Uri.TryCreate(RedirectUri, UriKind.Absolute, out var redirectUri) ||
                (redirectUri.Scheme != Uri.UriSchemeHttp && redirectUri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException(
                    $"OIDC_REDIRECT_URI must be an absolute http(s) URL, got '{RedirectUri}'.");

            if (!string.Equals(redirectUri.AbsolutePath.TrimEnd('/'), CallbackPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"OIDC_REDIRECT_URI must end in '{CallbackPath}' (that is the only path GarageStack handles the " +
                    $"provider's response on), got '{redirectUri.AbsolutePath}'.");
        }

        // A provider that never gets the openid scope returns a plain OAuth2 response with no
        // id_token, which fails deep inside the handler with an unhelpful message.
        if (!ScopeList.Contains("openid", StringComparer.OrdinalIgnoreCase))
            Scopes = $"openid {Scopes}".Trim();
    }

    private static string[] SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
