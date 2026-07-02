using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Demo;

namespace GarageStack.Api.Endpoints;

public static class DemoEndpoints
{
    public static IEndpointRouteBuilder MapDemoEndpoints(
        this IEndpointRouteBuilder app,
        ITelemetryRepository telemetry)
    {
        var demoRepo = telemetry as DemoTelemetryRepository
            ?? throw new InvalidOperationException(
                $"{nameof(MapDemoEndpoints)} requires {nameof(DemoTelemetryRepository)} to be registered as {nameof(ITelemetryRepository)} - check demo mode DI wiring in Program.cs.");

        var group = app.MapGroup("/api/demo")
            .WithTags("Demo")
            .RequireAuthorization();

        group.MapPost("/status/{vin}", async (
            string vin,
            DemoStatusOverrideDto dto,
            IVehicleRepository vehicles,
            CancellationToken ct) =>
        {
            var resolved = await VehicleEndpoints.ResolveVehicleAsync(vin, vehicles, ct);
            if (resolved.NotFound is not null) return resolved.NotFound;
            demoRepo.ApplyOverride(dto);
            return Results.NoContent();
        })
        .WithSummary("Override demo vehicle status fields");

        return app;
    }
}
