using GarageStack.Api.Services;
using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Api.Endpoints;

public static class TripEndpoints
{
    /// <summary>How many trips one purpose change may cover: a year of daily driving, with room to spare.</summary>
    internal const int MaxTripsPerPurposeChange = 1000;

    public static IEndpointRouteBuilder MapTripEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{vin}/trips")
            .WithTags("Trips")
            .RequireAuthorization()
            .AddEndpointFilter<ResolveVehicleFilter>();

        group.MapGet("/", async (
            HttpContext httpContext,
            ITripRepository trips,
            DateTimeOffset? from,
            DateTimeOffset? to,
            bool? points,
            CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            // Without their fixes a trip is one small row, so the statistics page can ask for years;
            // with them (the map) a period stays at most 90 days.
            var summariesOnly = points == false;
            var rangeError = DateRange.TryResolve(from, to, TimeSpan.FromDays(30),
                summariesOnly ? DateRange.LongestStatisticsSpan : TimeSpan.FromDays(90), out var start, out var end);
            if (rangeError is not null) return rangeError;

            return summariesOnly
                ? Results.Ok(await trips.GetSummariesAsync(vehicle.Id, start, end, ct))
                : Results.Ok(await trips.GetTripsAsync(vehicle.Id, start, end, ct));
        })
        .WithSummary("Get trip history (saved trips, then the ones not saved yet, including the trip being driven); points=false leaves out the fixes");

        // The dashboard shows one trip, so it asks for that one rather than a period of them.
        group.MapGet("/latest", async (HttpContext httpContext, ITripRepository trips, CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();
            var latest = await trips.GetLatestAsync(vehicle.Id, DateTime.UtcNow, ct);
            return latest is null ? Results.NoContent() : Results.Ok(latest);
        })
        .WithSummary("Get the newest trip: the one being driven, or else the last one driven");

        // Without the fixes, so a whole tax year fits in one answer.
        group.MapGet("/log", async (
            HttpContext httpContext,
            ITripRepository trips,
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            var rangeError = DateRange.TryResolve(from, to, TimeSpan.FromDays(31), TimeSpan.FromDays(366), out var start, out var end);
            if (rangeError is not null) return rangeError;

            return Results.Ok(await trips.GetLogAsync(vehicle.Id, start, end, ct));
        })
        .WithSummary("Get the trip log: saved trips with their purpose, notes, odometer readings and addresses");

        group.MapPut("/{id:long}", async (
            HttpContext httpContext,
            long id,
            TripLogUpdateRequest req,
            ITripRepository trips,
            CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            var notes = NormalizeNotes(req.Notes);
            if (notes is { Length: > Trip.NotesMaxLength })
                return ApiProblems.BadRequest("trip.notesTooLong", $"Notes must be {Trip.NotesMaxLength} characters or fewer");

            var entry = await trips.SetPurposeAndNotesAsync(vehicle.Id, id, req.Purpose, notes, ct);
            return entry is null ? Results.NotFound() : Results.Ok(entry);
        })
        .WithSummary("Record what a trip was for, and any notes");

        group.MapPost("/purpose", async (
            HttpContext httpContext,
            TripPurposeRequest req,
            ITripRepository trips,
            CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            if (req.Ids is not { Count: > 0 })
                return ApiProblems.BadRequest("trip.idsRequired", "ids must contain at least one trip");
            if (req.Ids.Count > MaxTripsPerPurposeChange)
                return ApiProblems.BadRequest("trip.tooManyIds", $"ids may not exceed {MaxTripsPerPurposeChange} trips");

            var changed = await trips.SetPurposeAsync(vehicle.Id, req.Ids, req.Purpose, ct);
            return Results.Ok(new TripPurposeResult(changed));
        })
        .WithSummary("Set the purpose of several trips at once, leaving their notes alone");

        group.MapPut("/{id:long}/hidden", async (
            HttpContext httpContext,
            long id,
            TripHiddenRequest req,
            ITripRepository trips,
            CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            if (req.Hidden is not { } hidden)
                return ApiProblems.BadRequest("trip.hiddenRequired", "hidden is required: true or false");

            var entry = await trips.SetHiddenAsync(vehicle.Id, id, hidden, ct);
            return entry is null ? Results.NotFound() : Results.Ok(entry);
        })
        .WithSummary("Hide a trip from the map, the statistics, the dashboard and the trip log's totals, or show it again");

        group.MapPost("/{id:long}/merge-next", async (
            HttpContext httpContext,
            long id,
            ITripRepository trips,
            CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            var result = await trips.MergeWithNextAsync(vehicle.Id, id, ct);
            return result.Outcome switch
            {
                TripMergeOutcome.Merged => Results.Ok(result.Merged),
                TripMergeOutcome.NoNextTrip => ApiProblems.Problem(StatusCodes.Status409Conflict, "trip.noNextTrip",
                    "No saved trip follows this one yet"),
                TripMergeOutcome.Unreadable => ApiProblems.Problem(StatusCodes.Status409Conflict, "trip.unreadable",
                    "The fixes of one of the two trips cannot be read"),
                _ => Results.NotFound(),
            };
        })
        .WithSummary("Join a trip with the saved trip after it, such as two trips a fuel stop split");

        // POST for the same reason as the map's reverse geocoding: a batch of ids, and a lookup
        // that may call upstream. The addresses found are kept on the trips.
        group.MapPost("/places", async (
            HttpContext httpContext,
            TripPlacesRequest req,
            TripPlaceService places,
            CancellationToken ct) =>
        {
            var vehicle = httpContext.ResolvedVehicle();

            if (req.Ids is not { Count: > 0 })
                return ApiProblems.BadRequest("trip.idsRequired", "ids must contain at least one trip");
            if (req.Ids.Count > TripPlaceService.MaxTripsPerRequest)
                return ApiProblems.BadRequest("trip.tooManyIds", $"ids may not exceed {TripPlaceService.MaxTripsPerRequest} trips");

            try
            {
                return Results.Ok(await places.ResolveAsync(
                    vehicle.Id, req.Ids, GeocodeDefaults.NormalizeLanguage(req.Language), ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The browser left the page; nothing is waiting for the answer.
                return Results.Ok(new TripPlacesResult([], Available: true, HasMore: true));
            }
        })
        .WithSummary("Look up the addresses at either end of trips, keeping them on the trips for good");

        return app;
    }

    // Blank notes are no notes, so clearing the field in the browser clears it in the log.
    internal static string? NormalizeNotes(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
}

public record TripLogUpdateRequest(TripPurpose? Purpose, string? Notes);

public record TripPurposeRequest(IReadOnlyList<long>? Ids, TripPurpose? Purpose);

public record TripPurposeResult(int Changed);

public record TripHiddenRequest(bool? Hidden);

public record TripPlacesRequest(IReadOnlyList<long>? Ids, string? Language);
