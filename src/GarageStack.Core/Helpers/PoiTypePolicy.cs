namespace GarageStack.Core.Helpers;

/// <summary>
/// Single place that decides which OpenStreetMap POI layers make sense for a given vehicle
/// type: fuel stations only for cars with a combustion engine, service areas and speed cameras
/// for everyone.
/// Used by the on-demand map endpoints and the Worker's pre-caching pass so they cannot drift.
/// Whether the deployment serves a layer at all is a separate question, answered by
/// OverpassApiClient.IsTypeEnabled.
/// </summary>
public static class PoiTypePolicy
{
    public const string Fuel = "fuel";
    public const string ServiceArea = "service_area";
    public const string SpeedCamera = "speed_camera";

    public static readonly IReadOnlyList<string> AllOverpassTypes = [Fuel, ServiceArea, SpeedCamera];

    /// <summary>
    /// Maps a requested POI type onto its constant, or null when unknown. Pass the result on
    /// instead of the raw request value so logs and queries downstream never carry user input.
    /// </summary>
    public static string? Normalize(string poiType) => poiType switch
    {
        Fuel => Fuel,
        ServiceArea => ServiceArea,
        SpeedCamera => SpeedCamera,
        _ => null,
    };

    public static bool IsAllowed(string poiType, string vehicleType) => poiType switch
    {
        Fuel => VehicleTypeHelper.HasCombustionEngine(vehicleType),
        ServiceArea => true,
        // Cameras enforce limits on whatever drives past them, so every car sees them.
        SpeedCamera => true,
        _ => false,
    };

    /// <summary>The Overpass POI types worth caching for <paramref name="vehicleType"/>.</summary>
    public static IEnumerable<string> AllowedOverpassTypes(string vehicleType) =>
        AllOverpassTypes.Where(t => IsAllowed(t, vehicleType));
}
