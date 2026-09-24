using GarageStack.Api.Services;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;

namespace GarageStack.Api.Endpoints;

public static class MapEndpoints
{
    private static IResult? ValidateLatLng(double lat, double lng) =>
        lat is < -90 or > 90 || lng is < -180 or > 180
            ? Results.BadRequest(new { error = "lat must be between -90 and 90, lng between -180 and 180" })
            : null;

    private static IResult? ValidateRadiusKm(double radiusKm, string paramName) =>
        radiusKm is < 1 or > 200
            ? Results.BadRequest(new { error = $"{paramName} must be between 1 and 200" })
            : null;

    private static IResult InvalidPoiType() =>
        Results.BadRequest(new { error = $"type must be '{PoiTypePolicy.Fuel}' or '{PoiTypePolicy.ServiceArea}'" });

    public static IEndpointRouteBuilder MapMapEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/map")
            .WithTags("Map")
            .RequireAuthorization();

        group.MapGet("/charging-stations", async (
            double lat,
            double lng,
            int distanceKm,
            int minPowerKw,
            int maxPowerKw,
            ChargingStationService svc,
            CancellationToken ct) =>
        {
            var error = ValidateLatLng(lat, lng) ?? ValidateRadiusKm(distanceKm, "distanceKm");
            if (error is not null) return error;

            var stations = await svc.GetStationsAsync(lat, lng, distanceKm, minPowerKw, maxPowerKw, ct);
            return Results.Ok(stations);
        })
        .WithSummary("Get nearby EV charging stations from Open Charge Map");

        group.MapGet("/poi/brands", async (
            string type,
            string vehicleType,
            PoiService svc,
            CancellationToken ct) =>
        {
            if (PoiTypePolicy.Normalize(type) is not { } poiType)
                return InvalidPoiType();

            if (!PoiTypePolicy.IsAllowed(poiType, vehicleType))
                return Results.Ok(Array.Empty<string>());

            var brands = await svc.GetBrandsAsync(poiType, ct);
            return Results.Ok(brands);
        })
        .WithSummary("Get distinct brand names from the cached POI dataset");

        group.MapGet("/poi", async (
            string type,
            double lat,
            double lng,
            double radiusKm,
            string vehicleType,
            PoiService svc,
            CancellationToken ct) =>
        {
            var error = ValidateLatLng(lat, lng) ?? ValidateRadiusKm(radiusKm, "radiusKm");
            if (error is not null) return error;

            if (PoiTypePolicy.Normalize(type) is not { } poiType)
                return InvalidPoiType();

            if (!PoiTypePolicy.IsAllowed(poiType, vehicleType))
                return Results.Ok(new PoiResult([], false));

            try
            {
                var result = await svc.GetPoisAsync(poiType, lat, lng, radiusKm, ct);
                return Results.Ok(result);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Results.Ok(new PoiResult([], false));
            }
        })
        .WithSummary("Get nearby POIs (fuel stations, service areas) from OSM Overpass cache");

        // POST rather than GET because a trip list asks about dozens of coordinates at once, and
        // one batched request per list beats one request per trip against the global rate limit.
        group.MapPost("/reverse", async (
            ReverseGeocodeRequest body,
            GeocodeService svc,
            CancellationToken ct) =>
        {
            var points = body.Points;
            if (points is null || points.Count == 0)
                return Results.BadRequest(new { error = "points must contain at least one coordinate" });

            if (points.Count > GeocodeDefaults.MaxPointsPerRequest)
                return Results.BadRequest(new { error = $"points may not exceed {GeocodeDefaults.MaxPointsPerRequest} coordinates" });

            foreach (var point in points)
                if (ValidateLatLng(point.Lat, point.Lng) is { } coordError)
                    return coordError;

            if (GeocodePrecisionPolicy.Normalize(body.Precision) is not { } precision)
                return Results.BadRequest(new { error = $"precision must be '{GeocodePrecisionPolicy.City}' or '{GeocodePrecisionPolicy.Address}'" });

            try
            {
                var result = await svc.ResolveAsync(points, precision, GeocodeDefaults.NormalizeLanguage(body.Language), ct);
                return Results.Ok(result);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The browser navigated away or the panel closed; nothing left to answer to.
                return Results.Ok(new ReverseGeocodeResult(points.Select(_ => (PlaceDto?)null).ToList(), true, true));
            }
        })
        .WithSummary("Reverse geocode coordinates to street and city names via the OSM Nominatim cache");

        return app;
    }
}

/// <param name="Points">Coordinates to resolve, at most <see cref="GeocodeDefaults.MaxPointsPerRequest"/> of them.</param>
/// <param name="Precision">"city" for a settlement name (trip list), "address" for street level (parked car).</param>
/// <param name="Language">Language for the place names; anything unsupported falls back to English.</param>
public sealed record ReverseGeocodeRequest(
    IReadOnlyList<GeoPoint>? Points,
    string? Precision,
    string? Language);
