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
}
