using Microsoft.Extensions.Configuration;

namespace GarageStack.Core.Configuration;

/// <summary>
/// The Overpass endpoint the fuel station, service area and speed camera layers come from
/// (OVERPASS__BASEURL), and whether the speed camera layer is served at all
/// (SPEEDCAMERAS__ENABLED=false, for the jurisdictions that restrict pointing a driver at camera
/// positions).
/// </summary>
public sealed class OverpassOptions
{
    public const string PublicBaseUrl = "https://overpass-api.de/api/interpreter";

    public string BaseUrl { get; init; } = PublicBaseUrl;

    public bool SpeedCamerasEnabled { get; init; } = true;

    /// <summary>A blank URL means the public instance; anything but an explicit "false" keeps the camera layer.</summary>
    public static OverpassOptions From(IConfiguration configuration) => new()
    {
        BaseUrl = configuration.TextOrDefault("Overpass:BaseUrl", PublicBaseUrl),
        SpeedCamerasEnabled = configuration.SwitchOrDefault("SpeedCameras:Enabled", fallback: true),
    };
}
