using GarageStack.Api.Reference;
using GarageStack.Api.Services;
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

        var gatewaySignIn = app.ServiceProvider.GetRequiredService<GatewaySignInMonitor>();
        group.MapPost("/gateway", async (DemoGatewayDto dto, CancellationToken ct) =>
            {
                await gatewaySignIn.UpdateAsync(dto.SignedIn, ct);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Make the demo gateway signed in to the MG account or not, as the Demo Controls panel does");

        return app;
    }
}

/// <summary>What the demo gateway should say about its MG sign-in.</summary>
/// <param name="SignedIn">False plays a failed sign-in, true a good one.</param>
public sealed record DemoGatewayDto(bool SignedIn);
