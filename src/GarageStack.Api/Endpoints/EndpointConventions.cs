using Microsoft.OpenApi;

namespace GarageStack.Api.Endpoints;

/// <summary>
/// What several endpoint groups share, each together with what the API reference says about it,
/// so the two cannot drift apart.
/// </summary>
internal static class EndpointConventions
{
    /// <summary>
    /// Resolves the {vin} route value with <see cref="ResolveVehicleFilter"/>, which answers 404
    /// for a VIN it does not know, and says so in the API reference.
    /// </summary>
    public static TBuilder ResolvesVehicle<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder
            .AddEndpointFilter<TBuilder, ResolveVehicleFilter>()
            .ProducesStatus(StatusCodes.Status404NotFound);

    /// <summary>
    /// An answer without a body, such as a filter's bare 401 or 404, for a whole group: the
    /// framework's own Produces only takes a single endpoint.
    /// </summary>
    public static TBuilder ProducesStatus<TBuilder>(this TBuilder builder, int statusCode) where TBuilder : IEndpointConventionBuilder =>
        // typeof(void), as the framework's own Produces passes for "no body": without a type the
        // answer is left out of the document.
        builder.WithMetadata(new ProducesResponseTypeMetadata(statusCode, typeof(void)));

    /// <summary>
    /// An endpoint whose 200 answer takes one of several shapes, chosen by its query: the API
    /// reference offers each of them (oneOf) instead of only the first.
    /// </summary>
    public static RouteHandlerBuilder ProducesOneOf(this RouteHandlerBuilder builder, params Type[] shapes) =>
        builder
            .Produces(StatusCodes.Status200OK, shapes[0])
            .AddOpenApiOperationTransformer(async (operation, context, ct) =>
            {
                var oneOf = new List<IOpenApiSchema>(shapes.Length);
                foreach (var shape in shapes)
                    oneOf.Add(await context.GetOrCreateSchemaAsync(shape, cancellationToken: ct));

                if (operation.Responses?.TryGetValue("200", out var ok) == true
                    && ok.Content?.TryGetValue("application/json", out var json) == true
                    && json is OpenApiMediaType media)
                    media.Schema = new OpenApiSchema { OneOf = oneOf };
            });
}
