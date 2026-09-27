using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;

namespace GarageStack.Core.Configuration;

/// <summary>
/// The names a deployment sets (MQTT_HOST, OIDC_AUTHORITY, ...) and the settings they fill. Docker
/// Compose, the all-in-one image and the Unraid template pass these names through as they are;
/// this table is the one place that translates them, so no deployment file restates a setting's
/// key or its default. A setting can still be given by its own key in .NET's environment form
/// (Mqtt__Host), which is how the all-in-one entrypoint passes the values it derives, and a few
/// settings are documented in that form (GEOCODING__ENABLED and the like).
/// </summary>
public static class EnvironmentAliases
{
    /// <summary>Deployment variable to the configuration key it fills.</summary>
    public static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // The broker both services connect to.
        ["MQTT_HOST"] = "Mqtt:Host",
        ["MQTT_PORT"] = "Mqtt:Port",
        ["MQTT_BROKER_USERNAME"] = "Mqtt:Username",
        ["MQTT_BROKER_PASSWORD"] = "Mqtt:Password",

        // Push notifications: the Worker signs and words them, the API hands out the public key.
        ["VAPID_PUBLIC_KEY"] = "Vapid:PublicKey",
        ["VAPID_PRIVATE_KEY"] = "Vapid:PrivateKey",
        ["NOTIFICATION_LANGUAGE"] = "Notifications:Culture",

        // Facts about the car the gateway cannot report.
        ["TYRE_PRESSURE_LOW_BAR"] = "TyrePressure:LowBar",
        ["TYRE_PRESSURE_GOOD_BAR"] = "TyrePressure:GoodBar",
        ["TYRE_PRESSURE_HIGH_BAR"] = "TyrePressure:HighBar",
        ["HV_BATTERY_CAPACITY_KWH"] = "HvBattery:CapacityKwh",

        // Who may call the API, and how often.
        ["CORS_ORIGIN"] = "Cors:Origins:0",
        ["RATE_LIMIT_GLOBAL_PER_MINUTE"] = "RateLimits:GlobalPerMinute",

        // Signing in.
        ["AUTH_USERNAME"] = "Auth:Username",
        ["AUTH_PASSWORD"] = "Auth:Password",
        ["AUTH_PASSWORD_LOGIN_ENABLED"] = "Auth:PasswordLoginEnabled",
        ["AUTH_COOKIE_SECURE"] = "Auth:CookieSecure",
        ["AUTH_SESSION_LIFETIME_HOURS"] = "Auth:SessionLifetimeHours",
        ["OIDC_AUTHORITY"] = "Oidc:Authority",
        ["OIDC_CLIENT_ID"] = "Oidc:ClientId",
        ["OIDC_CLIENT_SECRET"] = "Oidc:ClientSecret",
        ["OIDC_SCOPES"] = "Oidc:Scopes",
        ["OIDC_PROVIDER_NAME"] = "Oidc:ProviderName",
        ["OIDC_AUTO_LOGIN"] = "Oidc:AutoLogin",
        ["OIDC_ALLOWED_GROUPS"] = "Oidc:AllowedGroups",
        ["OIDC_ALLOWED_EMAILS"] = "Oidc:AllowedEmails",
        ["OIDC_GROUPS_CLAIM"] = "Oidc:GroupsClaim",
        ["OIDC_REDIRECT_URI"] = "Oidc:RedirectUri",
        ["OIDC_REQUIRE_HTTPS_METADATA"] = "Oidc:RequireHttpsMetadata",

        // Optional extras, each off until given a key.
        ["WIDGET_API_KEY"] = "Widget:ApiKey",
        ["OPENCHARGEMAP_API_KEY"] = "OpenChargeMap:ApiKey",
    };

    /// <summary>
    /// Adds the aliased variables that are set, as the last configuration source. A blank variable
    /// counts as unset: an empty Unraid field, or a compose file passing an unset variable through,
    /// leaves the setting at its default. When the environment also gives the setting by its own
    /// key, that key wins, so the values the all-in-one entrypoint derives (and compose files that
    /// still translate the names themselves) keep their meaning.
    /// </summary>
    /// <param name="builder">The configuration being assembled.</param>
    /// <param name="environment">The variables to read; the process environment when omitted.</param>
    public static IConfigurationBuilder AddGarageStackEnvironment(
        this IConfigurationBuilder builder, IDictionary? environment = null) =>
        builder.AddInMemoryCollection(Resolve(environment ?? Environment.GetEnvironmentVariables()));

    internal static Dictionary<string, string?> Resolve(IDictionary environment)
    {
        // Settings the environment gives by their own key: "Mqtt__Host" is "Mqtt:Host".
        var givenByKey = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry variable in environment)
        {
            if (variable.Key is string name && IsSet(variable.Value as string))
                givenByKey.Add(name.Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal));
        }

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (variable, key) in Map)
        {
            if (environment[variable] is string value && IsSet(value) && !givenByKey.Contains(key))
                values[key] = value;
        }

        return values;
    }

    private static bool IsSet([NotNullWhen(true)] string? value) => !string.IsNullOrWhiteSpace(value);
}
