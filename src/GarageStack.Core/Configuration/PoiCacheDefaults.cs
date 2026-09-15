namespace GarageStack.Core.Configuration;

/// <summary>
/// Shared settings for the map POI cache (charging stations, fuel stations, service areas),
/// used by the on-demand Api services and the Worker's background pre-caching alike.
/// </summary>
public static class PoiCacheDefaults
{
    /// <summary>How long a fetched tile stays valid before it is refreshed from upstream.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

    public const string OverpassSource = "overpass";
    public const string OcmSource = "ocm";
    public const string ChargingPoiType = "charging";
}
