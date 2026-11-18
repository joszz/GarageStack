using GarageStack.Api.Reference;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Demo;
using Scalar.AspNetCore;

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
            .WithBadge(ApiReferenceText.DemoOnlyBadge)
            .RequireAuthorization();

        group.MapGroup("/status/{vin}")
            .ResolvesVehicle()
            .MapPost("/", (DemoStatusOverrideDto dto) =>
            {
                demoRepo.ApplyOverride(dto);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Override demo vehicle status fields, as the Demo Controls panel does");

        return app;
    }
}
