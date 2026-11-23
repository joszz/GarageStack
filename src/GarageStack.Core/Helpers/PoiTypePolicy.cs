namespace GarageStack.Core.Helpers;

/// <summary>
/// Single place that decides which OpenStreetMap POI layers make sense for a given vehicle
/// type: fuel stations only for cars with a combustion engine, service areas, speed cameras,
/// parking, car washes, air pumps and garages for everyone.
/// Used by the on-demand map endpoints and the Worker's pre-caching pass so they cannot drift.
/// Whether the deployment serves a layer at all is a separate question, answered by
/// OverpassApiClient.IsTypeEnabled.
/// </summary>
public static class PoiTypePolicy
{
    public const string Fuel = "fuel";
    public const string ServiceArea = "service_area";
    public const string SpeedCamera = "speed_camera";
    public const string Parking = "parking";
    public const string CarWash = "car_wash";
    public const string CompressedAir = "compressed_air";
    public const string CarRepair = "car_repair";

    public static readonly IReadOnlyList<string> AllOverpassTypes =
        [Fuel, ServiceArea, SpeedCamera, Parking, CarWash, CompressedAir, CarRepair];

    /// <summary>
    /// The layers the Worker keeps cached around the car ahead of time. The others are looked up
    /// only once someone switches them on and pans the map there: every type pre-cached is another
    /// query on the shared public Overpass instance per tile around every car, every six hours.
    /// </summary>
    public static readonly IReadOnlySet<string> PreCachedOverpassTypes = new HashSet<string> { Fuel, ServiceArea, SpeedCamera };

    /// <summary>
    /// Maps a requested POI type onto its constant, or null when unknown. Pass the result on
    /// instead of the raw request value so logs and queries downstream never carry user input.
    /// </summary>
    public static string? Normalize(string poiType) => poiType switch
    {
        Fuel => Fuel,
        ServiceArea => ServiceArea,
        SpeedCamera => SpeedCamera,
        Parking => Parking,
        CarWash => CarWash,
        CompressedAir => CompressedAir,
        CarRepair => CarRepair,
        _ => null,
    };

    public static bool IsAllowed(string poiType, string vehicleType) => poiType switch
    {
        Fuel => VehicleTypeHelper.HasCombustionEngine(vehicleType),
        ServiceArea => true,
        // Cameras enforce limits on whatever drives past them, so every car sees them.
        SpeedCamera => true,
        // Every car parks, gets washed, has tyres and goes to a garage.
        Parking or CarWash or CompressedAir or CarRepair => true,
        _ => false,
    };

    /// <summary>The Overpass POI types worth caching ahead of time for <paramref name="vehicleType"/>.</summary>
    public static IEnumerable<string> PreCachedOverpassTypesFor(string vehicleType) =>
        AllOverpassTypes.Where(t => PreCachedOverpassTypes.Contains(t) && IsAllowed(t, vehicleType));
}
