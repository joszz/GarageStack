namespace GarageStack.Core.Helpers;

public static class TileHelper
{
    public static IReadOnlyList<(int CellLat, int CellLng)> ComputeTiles(
        double lat, double lng, double radiusKm)
    {
        var minLat = lat - radiusKm / 111.0;
        var maxLat = lat + radiusKm / 111.0;
        var lngFactor = 111.0 * Math.Cos(lat * Math.PI / 180.0);
        var minLng = lngFactor > 0 ? lng - radiusKm / lngFactor : lng - 1.0;
        var maxLng = lngFactor > 0 ? lng + radiusKm / lngFactor : lng + 1.0;

        var startCellLat = (int)Math.Floor(minLat * 2);
        var endCellLat = (int)Math.Floor(maxLat * 2);
        var startCellLng = (int)Math.Floor(minLng * 2);
        var endCellLng = (int)Math.Floor(maxLng * 2);

        var tiles = new List<(int, int)>();
        for (var clat = startCellLat; clat <= endCellLat; clat++)
            for (var clng = startCellLng; clng <= endCellLng; clng++)
                tiles.Add((clat, clng));
        return tiles;
    }
}
