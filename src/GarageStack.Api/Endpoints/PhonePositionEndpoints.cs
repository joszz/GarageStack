using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Api.Endpoints;

public static class PhonePositionEndpoints
{
    public const string RateLimitPolicy = "phone-positions";

    public static IEndpointRouteBuilder MapPhonePositionEndpoints(this IEndpointRouteBuilder app)
    {
        var time = app.ServiceProvider.GetRequiredService<TimeProvider>();
        app.MapGroup("/api/vehicles/{vin}/phone-positions")
            .WithTags("Phone GPS").RequireAuthorization().RequireRateLimiting(RateLimitPolicy)
            .ResolvesVehicle()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .MapPost("", async (HttpContext http, PhonePositionsRequest request, IPhonePositionRepository repo,
                CancellationToken ct) =>
            {
                if (!PhonePositionBatch.IsTrackerId(request.TrackerId))
                    return ApiProblems.BadRequest("phone.invalidTracker",
                        "Supply the device's tracker id: 8 to 64 letters, digits or dashes.");
                var vehicleId = http.ResolvedVehicle().Id;
                var positions = PhonePositionBatch.Parse(vehicleId, request.TrackerId!, request.Positions,
                    time.GetUtcNow().UtcDateTime);
                if (positions is null)
                    return ApiProblems.BadRequest("phone.invalidPositions",
                        "Supply at most 500 positions with valid coordinates, accuracy and speed.");
                var stored = await repo.AddAsync(vehicleId, request.TrackerId!, positions, ct);
                return TypedResults.Ok(new PhonePositionsStored(stored));
            })
            .Produces<PhonePositionsStored>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Store positions the installed app recorded on a phone in the car, to be added to its trips");
        return app;
    }
}

/// <summary>A batch of positions from one device.</summary>
/// <param name="TrackerId">The random id the app made for itself on the device.</param>
/// <param name="Positions">At most 500 positions. Those older than 7 days or more than 5 minutes ahead are left out.</param>
public sealed record PhonePositionsRequest(string? TrackerId, IReadOnlyList<PhonePositionInput>? Positions);

/// <summary>What the batch added.</summary>
/// <param name="Stored">How many positions were new; a batch sent again stores none.</param>
public sealed record PhonePositionsStored(int Stored);
