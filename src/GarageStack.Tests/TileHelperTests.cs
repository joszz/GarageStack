using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

public class TileHelperTests
{
    [Theory]
    [InlineData(89.999)]
    [InlineData(90.0)]
    [InlineData(-90.0)]
    public void ComputeTiles_NearPole_DoesNotExplode(double lat)
    {
        var tiles = TileHelper.ComputeTiles(lat, 0.0, 100);

        // Without the cos(lat)-near-zero guard, this would blow up into tens of
        // thousands of tiles instead of a small box around the pole.
        Assert.True(tiles.Count < 50, $"expected a small tile set near the pole, got {tiles.Count}");
    }

    [Fact]
    public void ComputeTiles_NormalLatitude_ReturnsReasonableTileCount()
    {
        var tiles = TileHelper.ComputeTiles(52.0, 5.0, 100);

        Assert.NotEmpty(tiles);
        Assert.True(tiles.Count < 50);
    }

    [Fact]
    public void ComputeTiles_KnownPoint_ReturnsContainingCell()
    {
        // lat=52.3, lng=4.9 → cellLat=floor(52.3*2)=104, cellLng=floor(4.9*2)=9
        var tiles = TileHelper.ComputeTiles(52.3, 4.9, 0.1);

        Assert.Contains((104, 9), tiles);
    }

    [Fact]
    public void ComputeTiles_LargeRadius_ReturnsMultipleCells()
    {
        // 100km radius should span several 0.5° cells
        var tiles = TileHelper.ComputeTiles(52.3, 4.9, 100.0);

        Assert.True(tiles.Count > 4);
    }
}
