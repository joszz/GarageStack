namespace GarageStack.Core.Helpers;

public static class TileHelper
{
    // Grid resolution: each cell covers 0.5 degrees of lat/lng (~55km at the equator).
    private const double CellsPerDegree = 2.0;

    public static (double MinLat, double MaxLat, double MinLng, double MaxLng) ComputeBounds(
        double lat, double lng, double radiusKm)
    {
        var minLat = lat - radiusKm / 111.0;
        var maxLat = lat + radiusKm / 111.0;
        var lngFactor = 111.0 * Math.Cos(lat * Math.PI / 180.0);
        var minLng = lngFactor > 0 ? lng - radiusKm / lngFactor : lng - 1.0;
        var maxLng = lngFactor > 0 ? lng + radiusKm / lngFactor : lng + 1.0;
        return (minLat, maxLat, minLng, maxLng);
    }

    public static (int CellLat, int CellLng) CellOf(double lat, double lng)
        => ((int)Math.Floor(lat * CellsPerDegree), (int)Math.Floor(lng * CellsPerDegree));

    public static IReadOnlyList<(int CellLat, int CellLng)> ComputeTiles(
        double lat, double lng, double radiusKm)
    {
        var (minLat, maxLat, minLng, maxLng) = ComputeBounds(lat, lng, radiusKm);

        var (startCellLat, startCellLng) = CellOf(minLat, minLng);
        var (endCellLat, endCellLng) = CellOf(maxLat, maxLng);

        var tiles = new List<(int, int)>();
        for (var clat = startCellLat; clat <= endCellLat; clat++)
            for (var clng = startCellLng; clng <= endCellLng; clng++)
                tiles.Add((clat, clng));
        return tiles;
    }

    /// <summary>Orders tiles by Manhattan distance to the cell containing (lat, lng) and takes the closest few.</summary>
    public static IReadOnlyList<(int CellLat, int CellLng)> ClosestTiles(
        IReadOnlyCollection<(int CellLat, int CellLng)> tiles, double lat, double lng, int count)
    {
        var (centerCellLat, centerCellLng) = CellOf(lat, lng);
        return tiles
            .OrderBy(t => Math.Abs(t.CellLat - centerCellLat) + Math.Abs(t.CellLng - centerCellLng))
            .Take(count)
            .ToList();
    }
}
