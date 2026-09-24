using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class PolylineCodecTests
{
    // The example from the format's own documentation, at five decimals: encoding and decoding
    // are pinned to a vector produced elsewhere, so a bug in one cannot hide in the other.
    private const string GoogleExample = "_p~iF~ps|U_ulLnnqC_mqNvxq`@";

    [Fact]
    public void Decode_KnownEncoding_YieldsItsCoordinates()
    {
        var decoded = PolylineCodec.Decode(GoogleExample, precision: 5);

        Assert.Equal(3, decoded.Count);
        Assert.Equal(38.5, decoded[0].Lat, 5);
        Assert.Equal(-120.2, decoded[0].Lng, 5);
        Assert.Equal(40.7, decoded[1].Lat, 5);
        Assert.Equal(-120.95, decoded[1].Lng, 5);
        Assert.Equal(43.252, decoded[2].Lat, 5);
        Assert.Equal(-126.453, decoded[2].Lng, 5);
    }

    [Fact]
    public void Encode_KnownCoordinates_MatchesTheirEncoding()
    {
        var encoded = PolylineCodec.Encode(
            [new GeoCoordinate(38.5, -120.2), new GeoCoordinate(40.7, -120.95), new GeoCoordinate(43.252, -126.453)],
            precision: 5);

        Assert.Equal(GoogleExample, encoded);
    }

    [Fact]
    public void RoundTrip_AtSixDecimals_KeepsEveryCoordinate()
    {
        // A trip's worth of vertices a few metres apart, which is the resolution a matched line
        // comes back at.
        var shape = Enumerable.Range(0, 500)
            .Select(i => new GeoCoordinate(52.512345 + i * 0.00004, 6.092345 - i * 0.00007))
            .ToList();

        var decoded = PolylineCodec.Decode(PolylineCodec.Encode(shape));

        Assert.Equal(shape.Count, decoded.Count);
        for (var i = 0; i < shape.Count; i++)
        {
            Assert.Equal(shape[i].Lat, decoded[i].Lat, 6);
            Assert.Equal(shape[i].Lng, decoded[i].Lng, 6);
        }
    }

    [Fact]
    public void Decode_TruncatedString_KeepsTheCompleteVerticesAndStops()
    {
        var encoded = PolylineCodec.Encode(
            [new GeoCoordinate(52.5, 6.09), new GeoCoordinate(52.51, 6.10), new GeoCoordinate(52.52, 6.11)]);

        var decoded = PolylineCodec.Decode(encoded[..^3]);

        Assert.InRange(decoded.Count, 1, 2);
        Assert.Equal(52.5, decoded[0].Lat, 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Decode_NothingToDecode_YieldsNoCoordinates(string? encoded)
        => Assert.Empty(PolylineCodec.Decode(encoded));
}
