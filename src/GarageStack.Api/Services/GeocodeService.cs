using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Services;

namespace GarageStack.Api.Services;

/// <summary>
/// Turns coordinates into places for the map and the trip list: cache first, and only what is
/// still missing from Nominatim. Upstream allows about one request per second, so a batch
/// resolves a few unknown coordinates at most and reports the rest as unresolved; the client
/// asks again and the answers fill in, which keeps a cold cache from blocking a page load.
/// </summary>
public sealed class GeocodeService(
    IGeocodeRepository repository,
    NominatimApiClient client,
    ILogger<GeocodeService> logger)
{
    /// <summary>
    /// Upstream lookups one request may perform. Each costs at least the client's minimum
    /// interval (about a second), so this is the ceiling on how long a caller waits.
    /// </summary>
    public const int MaxUpstreamLookupsPerRequest = 3;

    public async Task<ReverseGeocodeResult> ResolveAsync(
        IReadOnlyList<GeoPoint> points, string precision, string language, CancellationToken ct = default)
    {
        if (!client.IsEnabled)
            return new ReverseGeocodeResult(points.Select(_ => (PlaceDto?)null).ToList(), Available: false, HasMore: false);

        var cellOfPoint = new (int CellLat, int CellLng)[points.Count];
        // One upstream lookup answers for its whole cell, so remember the first point that fell
        // into each: that real coordinate is what gets sent, not the cell's corner.
        var probeByCell = new Dictionary<(int CellLat, int CellLng), GeoPoint>();
        for (var i = 0; i < points.Count; i++)
        {
            var cell = GeocodePrecisionPolicy.CellOf(points[i].Lat, points[i].Lng, precision);
            cellOfPoint[i] = cell;
            probeByCell.TryAdd(cell, points[i]);
        }

        var cached = await repository.GetValidAsync(precision, language, [.. probeByCell.Keys], ct);
        var placeByCell = cached.ToDictionary(e => (e.CellLat, e.CellLng), MapEntry);

        var lookups = 0;
        foreach (var (cell, probe) in probeByCell)
        {
            if (placeByCell.ContainsKey(cell)) continue;
            if (lookups >= MaxUpstreamLookupsPerRequest) break;

            lookups++;
            var place = await client.ReverseAsync(probe.Lat, probe.Lng, precision, language, ct);

            // Null means we could not ask (gate held, backoff window, upstream error). The next
            // cell would meet the same wall, so stop and let the client come back for the rest.
            if (place is null) break;

            await repository.UpsertAsync(precision, language, cell.CellLat, cell.CellLng, place,
                place.IsEmpty ? GeocodeDefaults.NegativeTtl : GeocodeDefaults.Ttl, ct);
            placeByCell[cell] = MapPlace(place);
        }

        var results = cellOfPoint.Select(cell => placeByCell.GetValueOrDefault(cell)).ToList();
        var unresolved = results.Count(r => r is null);
        if (unresolved > 0)
        {
            logger.LogDebug("Reverse geocoding resolved {Resolved}/{Total} {Precision} points, {Lookups} from upstream",
                results.Count - unresolved, results.Count, precision, lookups);
        }

        return new ReverseGeocodeResult(results, Available: true, HasMore: unresolved > 0);
    }

    private static PlaceDto MapEntry(GeocodeCacheEntry e) =>
        new(e.Road, e.HouseNumber, e.City, e.Postcode, e.CountryCode, e.DisplayName);

    private static PlaceDto MapPlace(PlaceAddress p) =>
        new(p.Road, p.HouseNumber, p.City, p.Postcode, p.CountryCode, p.DisplayName);
}

public sealed record GeoPoint(double Lat, double Lng);

/// <summary>
/// A resolved place. All-null fields mean upstream knows no place at that coordinate, which is
/// different from the null entry a <see cref="ReverseGeocodeResult"/> carries for one it has not
/// managed to look up yet.
/// </summary>
public sealed record PlaceDto(
    string? Road,
    string? HouseNumber,
    string? City,
    string? Postcode,
    string? CountryCode,
    string? DisplayName);

/// <param name="Results">One entry per requested point, in the same order; null where unresolved.</param>
/// <param name="Available">False when geocoding is switched off for this deployment, so a client can stop asking.</param>
/// <param name="HasMore">True while some points are still unresolved and a later request may fill them in.</param>
public sealed record ReverseGeocodeResult(
    IReadOnlyList<PlaceDto?> Results,
    bool Available,
    bool HasMore);
