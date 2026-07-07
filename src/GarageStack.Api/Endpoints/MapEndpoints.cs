using GarageStack.Api.Services;

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

    private static IResult? ValidatePoiType(string type) =>
        type is not ("fuel" or "service_area")
            ? Results.BadRequest(new { error = "type must be 'fuel' or 'service_area'" })
            : null;

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
            var error = ValidatePoiType(type);
            if (error is not null) return error;

            if (!PoiService.IsPoiTypeAllowed(type, vehicleType))
                return Results.Ok(Array.Empty<string>());

            var brands = await svc.GetBrandsAsync(type, ct);
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
            var error = ValidateLatLng(lat, lng) ?? ValidateRadiusKm(radiusKm, "radiusKm") ?? ValidatePoiType(type);
            if (error is not null) return error;

            if (!PoiService.IsPoiTypeAllowed(type, vehicleType))
                return Results.Ok(new PoiResult([], false));

            try
            {
                var result = await svc.GetPoisAsync(type, lat, lng, radiusKm, ct);
                return Results.Ok(result);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Results.Ok(new PoiResult([], false));
            }
        })
        .WithSummary("Get nearby POIs (fuel stations, service areas) from OSM Overpass cache");

        return app;
    }
}
