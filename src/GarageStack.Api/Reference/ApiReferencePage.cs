using GarageStack.Api.Authentication;
using Scalar.AspNetCore;

namespace GarageStack.Api.Reference;

/// <summary>
/// The API reference: Scalar's page for the OpenAPI document, at <see cref="Path"/>, in every
/// deployment. It sits under /api/ so it is served on the app's own address (through nginx, or
/// Vite in development), where the browser's session cookie and Origin are the app's: Test Request
/// then works for whoever is signed in, changes included, with nothing to fill in. Signed-in only,
/// like the document it shows.
/// </summary>
internal static class ApiReferencePage
{
    public const string Path = "/api/reference";

    // Scalar starts from an inline script, allowed by its nonce, and styles itself from script,
    // which takes inline styles. It loads nothing from anywhere else: its fonts are the system's,
    // and its telemetry and AI features are off. No 'unsafe-eval': Zod, inside the bundle, tries
    // eval once (the browser reports that) and validates without it.
    private const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self' 'nonce-{0}'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self'; worker-src 'self' blob:; " +
        "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>
    /// Sends a browser that is not signed in to the login page, which brings it back here, and
    /// gives the page its Content-Security-Policy. Between authentication and authorization, so
    /// the user is known and the redirect comes before authorization's bare 401. Nothing, while
    /// the page is switched off: a sign-in would only lead to its 404.
    /// </summary>
    public static IApplicationBuilder UseApiReferencePage(this IApplicationBuilder app, ApiReferenceOptions options) =>
        !options.Enabled ? app : app.UseWhen(
            context => context.Request.Path.StartsWithSegments(Path),
            branch => branch.Use(async (context, next) =>
            {
                if (context.User.Identity?.IsAuthenticated != true
                    && HttpMethods.IsGet(context.Request.Method)
                    && context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Redirect(AuthenticationSetup.LoginPageReturningTo(context.Request.Path + context.Request.QueryString));
                    return;
                }

                context.Response.OnStarting(() =>
                {
                    if (context.Items.TryGetValue(ScalarOptions.NonceHttpContextItemKey, out var value) && value is string nonce)
                        context.Response.Headers.ContentSecurityPolicy = string.Format(
                            System.Globalization.CultureInfo.InvariantCulture, ContentSecurityPolicy, nonce);
                    return Task.CompletedTask;
                });

                await next(context);
            }));

    /// <summary>
    /// The OpenAPI document, always, and the page that shows it when <paramref name="options"/>
    /// switch it on; both for signed-in callers only. Switched off, /api/reference answers 404.
    /// </summary>
    public static IEndpointRouteBuilder MapApiReferencePage(this IEndpointRouteBuilder app, ApiReferenceOptions options)
    {
        app.MapOpenApi(OpenApiSetup.DocumentRoute).RequireAuthorization();
        if (!options.Enabled)
            return app;

        app.MapScalarApiReference(Path, scalar => scalar
                .WithTitle("GarageStack API reference")
                .WithOpenApiRoutePattern(OpenApiSetup.DocumentRoute)
                .WithTheme(ScalarTheme.DeepSpace)
                .EnableDarkMode()
                .SortTagsAlphabetically()
                .SortOperationsByMethod()
                // The page's own address, whatever proxy it came through, so a request never
                // leaves for another scheme or host than the page was loaded from.
                .WithDynamicBaseServerUrl(true)
                .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl)
                // The session needs nothing filled in: the browser sends its cookie itself, and no
                // script can read or set it. This value only replaces the placeholder Scalar shows.
                .AddPreferredSecuritySchemes(OpenApiSetup.SessionScheme)
                .AddApiKeyAuthentication(OpenApiSetup.SessionScheme, scheme => scheme.Value = "sent by your browser")
                // Scalar's own toolbar and client button lead to its hosted services, not this API.
                .HideDeveloperTools()
                .HideClientButton()
                .DisableDefaultFonts()
                .DisableTelemetry()
                .DisableAgent()
                .DisableMcp()
                .WithNonce())
            .RequireAuthorization();

        return app;
    }
}
