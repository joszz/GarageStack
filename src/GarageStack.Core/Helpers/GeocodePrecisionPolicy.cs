namespace GarageStack.Core.Helpers;

/// <summary>
/// Single place that decides, per reverse-geocode precision, how coarse the cache grid is and
/// how much detail to ask upstream for. Two precisions earn their keep: a trip list only needs
/// the settlement name, and asking for that on a coarse grid means every visit to the same
/// street shares one cached answer, while the parked car's card wants the actual street and
/// house number and so needs a fine grid.
/// </summary>
public static class GeocodePrecisionPolicy
{
    public const string City = "city";
    public const string Address = "address";

    public static readonly IReadOnlyList<string> All = [City, Address];

    // Cells per degree of latitude/longitude. City: 0.005 deg, about 550m north-south, so
    // repeated stops at the same place reuse one answer while neighbouring villages keep theirs.
    // Address: 0.0005 deg, about 55m, fine enough that two sides of a street stay apart.
    private const double CityCellsPerDegree = 200.0;
    private const double AddressCellsPerDegree = 2000.0;

    /// <summary>
    /// Maps a requested precision onto its constant, or null when unknown. Pass the result on
    /// instead of the raw request value so logs and queries downstream never carry user input.
    /// </summary>
    public static string? Normalize(string? precision) => precision switch
    {
        City => City,
        Address => Address,
        _ => null,
    };

    /// <summary>Nominatim zoom level: 10 resolves to a settlement, 18 to a building.</summary>
    public static int ZoomFor(string precision) => precision switch
    {
        City => 10,
        Address => 18,
        _ => throw new ArgumentOutOfRangeException(nameof(precision), precision, "Not a geocode precision"),
    };

    private static double CellsPerDegree(string precision) => precision switch
    {
        City => CityCellsPerDegree,
        Address => AddressCellsPerDegree,
        _ => throw new ArgumentOutOfRangeException(nameof(precision), precision, "Not a geocode precision"),
    };

    /// <summary>
    /// The cache cell a coordinate falls into. Only the cache key is quantised: the coordinate
    /// actually sent upstream stays the caller's own, so the answer describes the real spot
    /// rather than a grid corner up to half a cell away.
    /// </summary>
    public static (int CellLat, int CellLng) CellOf(double lat, double lng, string precision)
    {
        var cells = CellsPerDegree(precision);
        return ((int)Math.Floor(lat * cells), (int)Math.Floor(lng * cells));
    }
}
