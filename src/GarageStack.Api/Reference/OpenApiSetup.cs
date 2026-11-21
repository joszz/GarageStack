using System.Text.Json.Nodes;
using GarageStack.Api.Authentication;
using GarageStack.Api.Endpoints;
using GarageStack.Api.Security;
using GarageStack.Core.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace GarageStack.Api.Reference;

/// <summary>
/// The OpenAPI document behind the API reference. Each endpoint declares its own summary and what
/// it answers; this adds what they all share: how a caller signs in, what an API token may call,
/// the refusals any request can meet, and the introduction and group descriptions the reference
/// shows. See documentation/API.md.
/// </summary>
internal static class OpenApiSetup
{
    public const string DocumentName = "v1";

    /// <summary>Under /api/, which the web interface's nginx (and Vite in development) passes on.</summary>
    public const string DocumentRoute = "/api/openapi/{documentName}.json";

    public const string SessionScheme = "session";
    public const string TokenScheme = "apiToken";
    public const string WidgetKeyScheme = "widgetKey";
    public const string CalendarTokenScheme = "calendarToken";

    private const string ProblemContentType = "application/problem+json";

    /// <summary>Where Scalar reads an operation's badges from.</summary>
    internal const string BadgesExtension = "x-badges";

    public static IServiceCollection AddGarageStackOpenApi(this IServiceCollection services, ReleaseVersion? version) =>
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                Describe(document, version);
                return Task.CompletedTask;
            });
            // The badges and code samples an endpoint declares (.WithBadge, .CodeSample) first, so
            // the shared answers below can add the token badge to them.
            options.AddScalarTransformers();
            options.AddOperationTransformer(AddSharedAnswersAsync);
            options.AddSchemaTransformer((schema, context, _) =>
            {
                Tidy(schema, context.JsonTypeInfo.Type);
                return Task.CompletedTask;
            });
        });

    private static void Describe(OpenApiDocument document, ReleaseVersion? version)
    {
        document.Info = new OpenApiInfo
        {
            Title = "GarageStack API",
            Version = version?.ToString() ?? "development",
            Description = ApiReferenceText.Introduction,
            License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://github.com/joszz/garagestack/blob/main/LICENSE") },
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            [SessionScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = AuthEndpoints.CookieName,
                Description = "The session cookie signing in sets. The browser sends it by itself, so this page needs nothing filled in.",
            },
            [TokenScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "gs_...",
                Description = "A read-only API token from Settings, API tokens. It only reads, and not the export, the logs or the tokens.",
            },
            [WidgetKeyScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = WidgetEndpoints.KeyHeader,
                Description = "WIDGET_API_KEY, for the Homepage widget only.",
            },
            [CalendarTokenScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Query,
                Name = CalendarEndpoints.TokenParameter,
                Description = "The token of the calendar link made in Settings, Calendar feed. It opens the feed and nothing else.",
            },
        };

        document.Tags ??= new HashSet<OpenApiTag>();
        foreach (var (name, description) in ApiReferenceText.Tags)
        {
            var tag = document.Tags.FirstOrDefault(t => t.Name == name);
            if (tag is null)
                document.Tags.Add(new OpenApiTag { Name = name, Description = description });
            else
                tag.Description = description;
        }
    }

    // What every endpoint can answer besides what it declares itself.
    private static async Task AddSharedAnswersAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var method = context.Description.HttpMethod ?? HttpMethods.Get;
        var path = new PathString("/" + context.Description.RelativePath);
        var problem = await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), cancellationToken: ct);
        operation.Responses ??= new OpenApiResponses();

        if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
        {
            var tokenRefusal = ApiTokenAuthentication.Refusal(method, path);
            operation.Security = [Requirement(SessionScheme, context.Document)];
            if (tokenRefusal is null)
            {
                operation.Security.Add(Requirement(TokenScheme, context.Document));
                AddBadge(operation, ApiReferenceText.TokenBadge);
            }

            Answer(operation, StatusCodes.Status401Unauthorized, "Not signed in, or the API token is unknown or revoked.");
            if (tokenRefusal is not null)
                Answer(operation, StatusCodes.Status403Forbidden, $"`{tokenRefusal.Code}`: {tokenRefusal.Message}.", problem);
        }

        if (CsrfOriginMiddleware.IsStateChanging(method))
            Answer(operation, StatusCodes.Status403Forbidden,
                $"`{CsrfOriginMiddleware.RefusedCode}`: sent from a page on an address that is not CORS_ORIGIN.", problem);

        Answer(operation, StatusCodes.Status429TooManyRequests, "Too many requests from this address; wait a minute and try again.");
    }

    // Adds a line to the answer for this status, or the answer itself when the endpoint declared none.
    private static void Answer(OpenApiOperation operation, int status, string description, IOpenApiSchema? problem = null)
    {
        var key = status.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (operation.Responses!.TryGetValue(key, out var existing) && existing is OpenApiResponse response)
        {
            // A declared answer carries the status's reason phrase until something says more.
            response.Description = response.Description == ReasonPhrases.GetReasonPhrase(status)
                ? description
                : $"{response.Description}\n\n{description}";
            return;
        }

        operation.Responses[key] = new OpenApiResponse
        {
            Description = description,
            Content = problem is null
                ? null
                : new Dictionary<string, IOpenApiMediaType> { [ProblemContentType] = new OpenApiMediaType { Schema = problem } },
        };
    }

    /// <summary>
    /// Adds a badge after those the endpoint declared, which Scalar's transformer has already
    /// written under the same extension.
    /// </summary>
    internal static void AddBadge(OpenApiOperation operation, string name)
    {
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        if (operation.Extensions.TryGetValue(BadgesExtension, out var existing) && existing is JsonNodeExtension { Node: JsonArray badges })
        {
            badges.Add(new JsonObject { ["name"] = name });
            return;
        }

        operation.Extensions[BadgesExtension] = new JsonNodeExtension(new JsonArray(new JsonObject { ["name"] = name }));
    }

    private static OpenApiSecurityRequirement Requirement(string scheme, OpenApiDocument? document) =>
        new() { [new OpenApiSecuritySchemeReference(scheme, document)] = [] };

    /// <summary>
    /// Makes the generated schemas say what the API sends. Numbers come out as integer-or-string
    /// with a pattern, because the API also reads "5" for 5, but it writes numbers and a client
    /// should send them. Doubles written by FiniteDoubleConverter come out with no type at all.
    /// A refusal's body is a ProblemDetails with the <c>code</c> ApiProblems adds.
    /// </summary>
    internal static void Tidy(OpenApiSchema schema, Type type)
    {
        if (schema.Type is { } kind && (kind.HasFlag(JsonSchemaType.Integer) || kind.HasFlag(JsonSchemaType.Number)))
        {
            schema.Type = kind & ~JsonSchemaType.String;
            schema.Pattern = null;
        }

        if (schema.Type is null && schema.Format is "double" or "float")
            schema.Type = Nullable.GetUnderlyingType(type) is null ? JsonSchemaType.Number : JsonSchemaType.Number | JsonSchemaType.Null;

        if (type == typeof(ProblemDetails) || type == typeof(HttpValidationProblemDetails))
        {
            schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
            schema.Properties["code"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Description = "Why, as a code that stays the same between releases, such as trip.changed. detail says it in English.",
            };
        }
    }
}
