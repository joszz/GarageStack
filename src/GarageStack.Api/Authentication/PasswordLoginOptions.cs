namespace GarageStack.Api.Authentication;

/// <summary>
/// The built-in single-account password login. It is the fallback for installs without an
/// identity provider: configuring OIDC switches it off, so an SSO install has a single way in
/// unless AUTH_PASSWORD_LOGIN_ENABLED deliberately asks for both (break-glass access for when
/// the provider is unreachable).
/// </summary>
public sealed class PasswordLoginOptions
{
    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public bool Enabled { get; init; }

    public bool Configured => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

    /// <summary>
    /// True when the password login was explicitly switched on or off, rather than following
    /// the default of "on unless OIDC is configured".
    /// </summary>
    public bool ExplicitlyConfigured { get; init; }

    /// <summary>
    /// Reads the credentials, preferring dedicated AUTH_* values and falling back to the MG
    /// account so existing installs keep working without touching their .env.
    /// </summary>
    internal static PasswordLoginOptions Resolve(IConfiguration config, bool oidcEnabled)
    {
        var username = FirstNonEmpty(
            config["Auth:Username"],
            config["AUTH_USERNAME"],
            config["SAIC_USER"],
            config["Saic:User"]);

        var password = FirstNonEmpty(
            config["Auth:Password"],
            config["AUTH_PASSWORD"],
            config["SAIC_PASSWORD"],
            config["Saic:Password"]);

        var configured = !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);

        // An explicit AUTH_PASSWORD_LOGIN_ENABLED wins either way; without it the login is on
        // only while no identity provider has taken over.
        var requested = config.GetValue<bool?>("Auth:PasswordLoginEnabled");

        return new PasswordLoginOptions
        {
            Username = username ?? string.Empty,
            Password = password ?? string.Empty,
            Enabled = configured && (requested ?? !oidcEnabled),
            ExplicitlyConfigured = requested.HasValue,
        };
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
