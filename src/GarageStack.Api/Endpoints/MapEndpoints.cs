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

        return app;
    }
}
