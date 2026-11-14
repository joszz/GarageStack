using GarageStack.Core.Configuration;

namespace GarageStack.Api.Reference;

/// <summary>
/// Whether the API reference page (<see cref="ApiReferencePage"/>) is served. It runs Scalar's
/// bundle on the app's own address with the visitor's session, so a production deployment only
/// serves it once API_REFERENCE_ENABLED says so. Demo mode and development serve it unless told
/// not to. The OpenAPI document itself is served either way, to sessions and API tokens.
/// </summary>
public sealed record ApiReferenceOptions(bool Enabled)
{
    public const string SettingKey = "ApiReference:Enabled";

    // The environment the Demo launch profile runs in.
    private const string DemoEnvironment = "Demo";

    public static ApiReferenceOptions From(IConfiguration configuration, string environmentName, bool isDemoMode) =>
        new(configuration.SwitchOrDefault(
            SettingKey,
            fallback: isDemoMode
                || environmentName == Environments.Development
                || environmentName == DemoEnvironment));
}

/// <summary>Whether this deployment serves the API reference page, for the link under the token settings.</summary>
public sealed record ApiReferenceStatus(bool Enabled);
