using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Services;

namespace GarageStack.Api.Endpoints;

/// <summary>
/// The car's Dutch registration: its plate, and what the RDW says about it (APK, insurance,
/// recalls). Both answer 404 with <see cref="DisabledCode"/> while RDW_ENABLED is off, which is
/// also how the browser learns to leave the plate field and the card out.
/// </summary>
public static class RdwEndpoints
{
    public const string DisabledCode = "rdw.disabled";

    public static IEndpointRouteBuilder MapRdwEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{vin}")
            .WithTags("RDW")
            .RequireAuthorization()
            .AddEndpointFilter<ResolveVehicleFilter>();

        // Deployment configuration, not request input.
        var options = app.ServiceProvider.GetRequiredService<RdwOptions>();
        var time = app.ServiceProvider.GetRequiredService<TimeProvider>();

        // A GET, so read-only API tokens can read it too.
        group.MapGet("/rdw", async (HttpContext httpContext, IRdwRepository rdw, CancellationToken ct) =>
        {
            if (!options.Enabled) return Disabled();

            var vehicle = httpContext.ResolvedVehicle();
            var info = await rdw.GetAsync(vehicle.Id, ct);
            return Results.Ok(ToDto(vehicle.LicensePlate, info, time.GetUtcNow().UtcDateTime));
        })
        .WithSummary("The vehicle's plate and what the RDW last said about it");

        group.MapPut("/license-plate", async (
            HttpContext httpContext,
            LicensePlateRequest req,
            IVehicleRepository vehicles,
            IRdwRepository rdw,
            RdwRefresher refresher,
            CancellationToken ct) =>
        {
            if (!options.Enabled) return Disabled();

            var vehicle = httpContext.ResolvedVehicle();
            var plate = LicensePlate.Normalize(req.LicensePlate);
            if (plate is not null && !LicensePlate.IsValid(plate))
                return ApiProblems.BadRequest("rdw.invalidPlate",
                    $"licensePlate must be letters and digits, {LicensePlate.MaxLength} at most once dashes and spaces are left out");

            if (plate != vehicle.LicensePlate)
            {
                // What the RDW said about the old plate is about another car.
                await vehicles.SetLicensePlateAsync(vehicle.Id, plate, ct);
                await rdw.DeleteAsync(vehicle.Id, ct);
                vehicle.LicensePlate = plate;
            }

            // Asked straight away, so the card has something to show before the Worker's next round.
            var info = plate is null ? null : await refresher.RefreshAsync(vehicle, ct);
            return Results.Ok(ToDto(plate, info, time.GetUtcNow().UtcDateTime));
        })
        .WithSummary("Set or clear the vehicle's Dutch plate, and look it up at the RDW right away");

        return app;
    }

    private static IResult Disabled() =>
        ApiProblems.Problem(StatusCodes.Status404NotFound, DisabledCode,
            "RDW lookups are switched off on this deployment (RDW_ENABLED)");

    internal static RdwStatusDto ToDto(string? licensePlate, RdwVehicleInfo? info, DateTime utcNow)
    {
        // A lookup for another plate is about another car: the plate changed while it ran.
        if (licensePlate is null || info is null || info.LicensePlate != licensePlate)
            return new RdwStatusDto(licensePlate, null);

        var recalls = RdwRecalls.Parse(info.RecallsJson)
            .Select(r => new RdwRecallDto(r.Code, r.IsOpen, r.Defect, r.Risk, r.Repair, r.Severity, r.FirstSeenAt))
            .ToList();
        return new RdwStatusDto(licensePlate, new RdwInfoDto(
            info.Found,
            info.Make,
            info.Model,
            info.ApkExpiresOn,
            // Counted on the Dutch calendar, as the notifications count it.
            info.ApkExpiresOn is { } expiresOn ? ApkReminder.DaysLeft(expiresOn, utcNow) : null,
            info.WamInsured,
            info.OpenRecallIndicator,
            recalls,
            info.FetchedAt,
            info.LastError));
    }
}

public record LicensePlateRequest(string? LicensePlate);

/// <summary>The plate (null when none is set) and the lookup for it (null until there is one).</summary>
public record RdwStatusDto(string? LicensePlate, RdwInfoDto? Info);

/// <summary>
/// What the RDW said. <see cref="Found"/> false means it knows no vehicle by the plate.
/// <see cref="FetchedAt"/> is the last answer, <see cref="LastError"/> why the attempt after it failed.
/// </summary>
public record RdwInfoDto(
    bool Found,
    string? Make,
    string? Model,
    DateOnly? ApkExpiresOn,
    int? ApkDaysLeft,
    bool? WamInsured,
    bool? OpenRecallIndicator,
    IReadOnlyList<RdwRecallDto> Recalls,
    DateTime? FetchedAt,
    string? LastError);

/// <summary>A recall, open or repaired; the texts are the RDW's, in Dutch.</summary>
public record RdwRecallDto(
    string Code, bool Open, string? Defect, string? Risk, string? Repair, string? Severity, DateTime FirstSeenAt);
