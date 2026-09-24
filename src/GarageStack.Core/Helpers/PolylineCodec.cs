using System.Text;
using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// The encoded-polyline format the matcher answers in and this API passes on: a line's
/// coordinates as one string of deltas instead of a JSON array per point. A snapped trip is a
/// few thousand vertices, which is tens of kilobytes as JSON and a few as a polyline - worth it
/// both on the wire and in the cache table.
/// <para>
/// Valhalla encodes at six decimals (about 10 cm), so that is the default here too. Decoding is
/// deliberately forgiving: a truncated string yields the vertices that were complete rather than
/// an exception, since the input comes from a third party.
/// </para>
/// </summary>
public static class PolylineCodec
{
    public const int DefaultPrecision = 6;

    public static IReadOnlyList<GeoCoordinate> Decode(string? encoded, int precision = DefaultPrecision)
    {
        if (string.IsNullOrEmpty(encoded)) return [];

        var factor = Math.Pow(10, precision);
        var coordinates = new List<GeoCoordinate>();
        var index = 0;
        long lat = 0, lng = 0;

        while (index < encoded.Length)
        {
            if (!TryDecodeValue(encoded, ref index, out var latDelta)) break;
            if (!TryDecodeValue(encoded, ref index, out var lngDelta)) break;
            lat += latDelta;
            lng += lngDelta;
            coordinates.Add(new GeoCoordinate(lat / factor, lng / factor));
        }

        return coordinates;
    }

    public static string Encode(IReadOnlyList<GeoCoordinate> coordinates, int precision = DefaultPrecision)
    {
        var factor = Math.Pow(10, precision);
        var builder = new StringBuilder(coordinates.Count * 8);
        long lat = 0, lng = 0;

        foreach (var coordinate in coordinates)
        {
            // Deltas are taken between rounded values, not rounded after subtracting, so that
            // decoding returns exactly the coordinates encoded here rather than drifting.
            var scaledLat = (long)Math.Round(coordinate.Lat * factor);
            var scaledLng = (long)Math.Round(coordinate.Lng * factor);
            EncodeValue(builder, scaledLat - lat);
            EncodeValue(builder, scaledLng - lng);
            lat = scaledLat;
            lng = scaledLng;
        }

        return builder.ToString();
    }

    private static bool TryDecodeValue(string encoded, ref int index, out long value)
    {
        value = 0;
        var shift = 0;
        while (true)
        {
            if (index >= encoded.Length) return false;
            var chunk = encoded[index++] - 63;
            if (chunk < 0) return false;
            value |= (long)(chunk & 0x1f) << shift;
            if (chunk < 0x20) break;
            shift += 5;
            // A value never needs more than six chunks; anything longer is a malformed string
            // rather than a very large number, and would otherwise shift off the end of the long.
            if (shift > 30) return false;
        }

        // Negative numbers are stored one's-complemented and shifted left by one.
        value = (value & 1) != 0 ? ~(value >> 1) : value >> 1;
        return true;
    }

    private static void EncodeValue(StringBuilder builder, long value)
    {
        var shifted = value < 0 ? ~(value << 1) : value << 1;
        while (shifted >= 0x20)
        {
            builder.Append((char)((0x20 | (int)(shifted & 0x1f)) + 63));
            shifted >>= 5;
        }
        builder.Append((char)((int)shifted + 63));
    }
}
