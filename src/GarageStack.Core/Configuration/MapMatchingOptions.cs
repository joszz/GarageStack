using Microsoft.Extensions.Configuration;

namespace GarageStack.Core.Configuration;

/// <summary>
/// Where trips are snapped onto roads, and whether they may be at all (MAPMATCHING__ENABLED,
/// MAPMATCHING__BASEURL). A deployment that would rather not send trip geometry to a third party
/// switches it off, or keeps snapped trips by pointing it at its own Valhalla.
/// </summary>
public sealed class MapMatchingOptions
{
    public const string PublicBaseUrl = "https://valhalla1.openstreetmap.de";

    public bool Enabled { get; init; } = true;

    public string BaseUrl { get; init; } = PublicBaseUrl;

    /// <summary>Anything but an explicit "false" leaves matching on; a blank URL means the public instance.</summary>
    public static MapMatchingOptions From(IConfiguration configuration) => new()
    {
        Enabled = configuration.SwitchOrDefault("MapMatching:Enabled", fallback: true),
        BaseUrl = configuration.TextOrDefault("MapMatching:BaseUrl", PublicBaseUrl),
    };
}
