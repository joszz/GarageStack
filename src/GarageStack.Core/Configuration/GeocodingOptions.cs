using Microsoft.Extensions.Configuration;

namespace GarageStack.Core.Configuration;

/// <summary>
/// Where reverse geocoding asks, and whether it may ask at all (GEOCODING__ENABLED,
/// GEOCODING__BASEURL). A deployment that would rather not send coordinates to a third party
/// switches it off, or keeps place names by pointing it at its own Nominatim.
/// </summary>
public sealed class GeocodingOptions
{
    public const string PublicBaseUrl = "https://nominatim.openstreetmap.org";

    public bool Enabled { get; init; } = true;

    public string BaseUrl { get; init; } = PublicBaseUrl;

    /// <summary>Anything but an explicit "false" leaves geocoding on; a blank URL means the public instance.</summary>
    public static GeocodingOptions From(IConfiguration configuration) => new()
    {
        Enabled = configuration.SwitchOrDefault("Geocoding:Enabled", fallback: true),
        BaseUrl = configuration.TextOrDefault("Geocoding:BaseUrl", PublicBaseUrl),
    };
}
