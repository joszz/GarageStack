using GarageStack.Api.Services;

namespace GarageStack.Api.Endpoints;

public static class MapEndpoints
{
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
            if (distanceKm is < 1 or > 200)
                return Results.BadRequest(new { error = "distanceKm must be between 1 and 200" });

            var stations = await svc.GetStationsAsync(lat, lng, distanceKm, minPowerKw, maxPowerKw, ct);
            return Results.Ok(stations);
        })
        .WithSummary("Get nearby EV charging stations from Open Charge Map");

        group.MapGet("/poi", async (
            string type,
            double lat,
            double lng,
            double radiusKm,
            string vehicleType,
            PoiService svc,
            CancellationToken ct) =>
        {
            if (radiusKm is < 1 or > 200)
                return Results.BadRequest(new { error = "radiusKm must be between 1 and 200" });

            if (type is not ("fuel" or "service_area"))
                return Results.BadRequest(new { error = "type must be 'fuel' or 'service_area'" });

            if (!PoiService.IsPoiTypeAllowed(type, vehicleType))
                return Results.Ok(Array.Empty<PoiItemDto>());

            var pois = await svc.GetPoisAsync(type, lat, lng, radiusKm, ct);
            return Results.Ok(pois);
        })
        .WithSummary("Get nearby POIs (fuel stations, service areas) from OSM Overpass cache");

        return app;
    }
}
