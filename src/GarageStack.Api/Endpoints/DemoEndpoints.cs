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

        group.MapGroup("/status/{vin}")
            .AddEndpointFilter<VehicleEndpoints.ResolveVehicleFilter>()
            .MapPost("/", (DemoStatusOverrideDto dto) =>
            {
                demoRepo.ApplyOverride(dto);
                return Results.NoContent();
            })
            .WithSummary("Override demo vehicle status fields");

        return app;
    }
}
