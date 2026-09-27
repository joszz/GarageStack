using GarageStack.Api.Endpoints;
using Serilog;

namespace GarageStack.Api.Security;

/// <summary>
/// Refuses a state-changing request whose Origin header names a site other than the configured
/// ones. A request without an Origin (a script, the homepage widget) passes: this backs up
/// SameSite=Strict for browsers rather than replacing it. See
/// <see cref="Hosting.ApiHostingExtensions.UseCsrfOriginCheck"/>.
/// </summary>
internal sealed class CsrfOriginMiddleware(RequestDelegate next, IReadOnlyList<string> allowedOrigins)
{
    private readonly string _allowedForLog = string.Join(", ", allowedOrigins);

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsStateChanging(context.Request.Method))
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin) && !CsrfPolicy.IsOriginAllowed(origin, allowedOrigins))
            {
                Log.Warning(
                    "CSRF origin check failed: request Origin '{Origin}' not in allowed list ({Allowed}). " +
                    "If you are accessing from a LAN device, set CORS_ORIGIN to match the address in your browser.",
                    origin.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal),
                    _allowedForLog);
                await ApiProblems.Problem(StatusCodes.Status403Forbidden, "csrf.originNotAllowed",
                        "Origin not allowed. Set CORS_ORIGIN to the address you use to reach the app.")
                    .ExecuteAsync(context);
                return;
            }
        }

        await next(context);
    }

    private static bool IsStateChanging(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);
}
