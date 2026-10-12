using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Api.Endpoints;

/// <summary>
/// A vehicle's home and work places. Every change re-labels the trips nobody chose a purpose for,
/// so the trip log matches the places straight away, past trips included.
/// </summary>
public static class CommutePlaceEndpoints
{
    public static IEndpointRouteBuilder MapCommutePlaceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{vin}/commute-places")
            .WithTags("Commute places")
            .RequireAuthorization()
            .AddEndpointFilter<ResolveVehicleFilter>();

        group.MapGet("/", async (HttpContext httpContext, ICommutePlaceRepository places, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();
            return Results.Ok((await places.ListAsync(vehicle.Id, ct)).Select(ToDto));
        })
        .WithSummary("List the vehicle's home and work places, home first");

        group.MapPost("/", async (
            HttpContext httpContext, CommutePlaceRequest req, ICommutePlaceRepository places, ITripRepository trips, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            if (Validate(req) is { } error) return ApiProblems.BadRequest(error);
            if (await places.CountAsync(vehicle.Id, ct) >= CommutePlaceLimits.MaxPerVehicle)
                return ApiProblems.BadRequest("commutePlace.limitReached",
                    $"A vehicle can have at most {CommutePlaceLimits.MaxPerVehicle} home and work places");

            var place = new CommutePlace { VehicleId = vehicle.Id };
            Apply(place, req);
            await places.AddAsync(place, ct);

            return Results.Ok(await ChangedAsync(vehicle.Id, place, places, trips, ct));
        })
        .WithSummary("Add a home or work place, and label the trips between them as commutes");

        group.MapPut("/{id:int}", async (
            HttpContext httpContext, int id, CommutePlaceRequest req, ICommutePlaceRepository places, ITripRepository trips, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            if (Validate(req) is { } error) return ApiProblems.BadRequest(error);

            var place = await places.UpdateAsync(vehicle.Id, id, p => Apply(p, req), ct);
            return place is null ? Results.NotFound() : Results.Ok(await ChangedAsync(vehicle.Id, place, places, trips, ct));
        })
        .WithSummary("Move, rename or resize a home or work place, and re-label the trips");

        group.MapDelete("/{id:int}", async (
            HttpContext httpContext, int id, ICommutePlaceRepository places, ITripRepository trips, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();
            return await places.DeleteAsync(vehicle.Id, id, ct)
                ? Results.Ok(await ChangedAsync(vehicle.Id, null, places, trips, ct))
                : Results.NotFound();
        })
        .WithSummary("Remove a home or work place, and re-label the trips");

        return app;
    }

    private static async Task<CommutePlaceChange> ChangedAsync(
        int vehicleId, CommutePlace? place, ICommutePlaceRepository places, ITripRepository trips, CancellationToken ct)
    {
        var relabel = await trips.RelabelCommutesAsync(vehicleId, await places.ListAsync(vehicleId, ct), ct);
        return new CommutePlaceChange(place is null ? null : ToDto(place), relabel.Marked, relabel.Cleared);
    }

    internal static ValidationError? Validate(CommutePlaceRequest req)
    {
        if (req.Kind is null)
            return new("commutePlace.kindRequired", "kind is required: home or work");
        if (req.Latitude is not { } lat || !double.IsFinite(lat) || lat is < -90 or > 90
            || req.Longitude is not { } lon || !double.IsFinite(lon) || lon is < -180 or > 180)
            return new("commutePlace.invalidPosition", "latitude and longitude are required and must be a position on Earth");
        if (req.RadiusMeters is < CommutePlaceLimits.MinRadiusMeters or > CommutePlaceLimits.MaxRadiusMeters)
            return new("commutePlace.invalidRadius",
                $"radiusMeters must be between {CommutePlaceLimits.MinRadiusMeters} and {CommutePlaceLimits.MaxRadiusMeters}");
        if (req.Label?.Trim().Length > CommutePlaceLimits.LabelMaxLength)
            return new("commutePlace.labelTooLong", $"label may not exceed {CommutePlaceLimits.LabelMaxLength} characters");
        return null;
    }

    // Only called once Validate has passed.
    private static void Apply(CommutePlace place, CommutePlaceRequest req)
    {
        place.Kind = req.Kind!.Value;
        place.Label = string.IsNullOrWhiteSpace(req.Label) ? null : req.Label.Trim();
        place.Latitude = req.Latitude!.Value;
        place.Longitude = req.Longitude!.Value;
        place.RadiusMeters = req.RadiusMeters ?? CommutePlaceLimits.DefaultRadiusMeters;
    }

    private static CommutePlaceDto ToDto(CommutePlace p) =>
        new(p.Id, p.Kind, p.Label, p.Latitude, p.Longitude, p.RadiusMeters);
}

public record CommutePlaceRequest(CommutePlaceKind? Kind, string? Label, double? Latitude, double? Longitude, int? RadiusMeters);

public record CommutePlaceDto(int Id, CommutePlaceKind Kind, string? Label, double Latitude, double Longitude, int RadiusMeters);

/// <summary>The place as saved (null once removed), and how many trips the change marked and unmarked as commutes.</summary>
public record CommutePlaceChange(CommutePlaceDto? Place, int Marked, int Cleared);
