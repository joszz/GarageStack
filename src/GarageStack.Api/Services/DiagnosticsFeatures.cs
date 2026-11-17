using GarageStack.Api.Authentication;
using GarageStack.Api.Reference;
using GarageStack.Core.Configuration;

namespace GarageStack.Api.Services;

/// <summary>
/// How this server is set up, for the diagnostics download: demo mode or not, and each optional
/// part switched on or off. Only whether, never a value: no address, key or account goes in.
/// </summary>
public sealed record DiagnosticsFeatures(bool DemoMode, IReadOnlyDictionary<string, bool> Switches)
{
    /// <summary>Read from the options the API registered, once, when it starts.</summary>
    public static DiagnosticsFeatures From(IServiceProvider services, bool isDemoMode) => new(isDemoMode, new SortedDictionary<string, bool>(StringComparer.Ordinal)
    {
        ["apiReference"] = services.GetRequiredService<ApiReferenceOptions>().Enabled,
        ["dawarich"] = services.GetRequiredService<DawarichOptions>().Enabled,
        ["geocoding"] = services.GetRequiredService<GeocodingOptions>().Enabled,
        ["mapMatching"] = services.GetRequiredService<MapMatchingOptions>().Enabled,
        ["oidc"] = services.GetRequiredService<OidcOptions>().Enabled,
        ["openChargeMap"] = services.GetRequiredService<OpenChargeMapOptions>().IsConfigured,
        ["passwordLogin"] = services.GetRequiredService<PasswordLoginOptions>().Enabled,
        ["pushNotifications"] = services.GetRequiredService<VapidOptions>().IsConfigured,
        ["rdw"] = services.GetRequiredService<RdwOptions>().Enabled,
        ["speedCameras"] = services.GetRequiredService<OverpassOptions>().SpeedCamerasEnabled,
        ["updateCheck"] = services.GetRequiredService<UpdateCheckOptions>().Enabled,
    });
}
