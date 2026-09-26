using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Api.Endpoints;

/// <summary>
/// Resolves the {vin} route value to a Vehicle before the handler runs, short-circuiting with 404
/// when no such vehicle exists. Shared by every endpoint group that takes a {vin} route parameter
/// (vehicles, trips, maintenance, widget, demo); handlers read the result with
/// <see cref="ResolvedVehicleExtensions.ResolvedVehicle"/>.
/// </summary>
public sealed class ResolveVehicleFilter : IEndpointFilter
{
    internal const string VehicleItemKey = "GarageStack.ResolvedVehicle";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var vin = context.HttpContext.Request.RouteValues["vin"] as string
            ?? throw new InvalidOperationException($"{nameof(ResolveVehicleFilter)} requires a {{vin}} route parameter.");

        var vehicles = context.HttpContext.RequestServices.GetRequiredService<IVehicleRepository>();
        var vehicle = await vehicles.GetByVinAsync(vin, context.HttpContext.RequestAborted);
        if (vehicle is null) return Results.NotFound();

        context.HttpContext.Items[VehicleItemKey] = vehicle;
        return await next(context);
    }
}

public static class ResolvedVehicleExtensions
{
    /// <summary>The vehicle <see cref="ResolveVehicleFilter"/> found for this request's {vin}.</summary>
    public static Vehicle ResolvedVehicle(this HttpContext httpContext) =>
        (Vehicle)httpContext.Items[ResolveVehicleFilter.VehicleItemKey]!;
}
