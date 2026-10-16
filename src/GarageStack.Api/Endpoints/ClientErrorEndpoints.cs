using GarageStack.Core.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace GarageStack.Api.Endpoints;

/// <summary>
/// Errors the web app ran into in someone's browser, written to the API's log. A script error or
/// a failed promise in the app otherwise never reached a log file, so a bug only one browser hit
/// stayed invisible. Signed-in only, rate-limited and size-capped, since the content is whatever
/// the page sends.
/// </summary>
public static class ClientErrorEndpoints
{
    internal const int MaxMessageLength = 500;
    internal const int MaxStackLength = 4000;
    internal const int MaxPathLength = 300;
    private const int MaxRequestBytes = 16 * 1024;

    public static IEndpointRouteBuilder MapClientErrorEndpoints(this IEndpointRouteBuilder app)
    {
        // Resolved once here: a handler parameter would count as request input (CodeQL).
        var logger = app.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("GarageStack.ClientErrors");

        app.MapPost("/api/client-errors", (ClientErrorReport report) =>
        {
            if (string.IsNullOrWhiteSpace(report.Message))
                return ApiProblems.BadRequest("clientError.messageRequired", "message is required");

            var entry = ToLogEntry(report);
            logger.LogWarning("Browser error on {Page} in {Source}: {Message}{Stack}",
                entry.Page, entry.Source, entry.Message, entry.Stack);
            return Results.NoContent();
        })
        .WithTags("Diagnostics")
        .WithSummary("Record an error the web app ran into in the browser")
        .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
        .RequireRateLimiting("client-errors")
        .RequireAuthorization();

        return app;
    }

    /// <summary>
    /// The report as it may enter the log: every field capped, each one on a single line (a
    /// stack keeps its frames apart with " | ") so a crafted report cannot forge log entries, and
    /// any VIN in a path shortened like everywhere else.
    /// </summary>
    internal static ClientErrorReport ToLogEntry(ClientErrorReport report)
    {
        var stack = OneLine(report.Stack.Truncate(MaxStackLength), " | ");
        return new ClientErrorReport(
            Message: OneLine(report.Message.Truncate(MaxMessageLength), " "),
            Stack: string.IsNullOrEmpty(stack) ? "" : $" | {stack}",
            Page: LogRedaction.RequestPath(OneLine(report.Page.Truncate(MaxPathLength), " ") ?? "?"),
            Source: LogRedaction.RequestPath(OneLine(report.Source.Truncate(MaxPathLength), " ") ?? "?"));
    }

    private static string? OneLine(string? text, string separator) =>
        text?.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", separator).Trim();
}

/// <param name="Message">The error's message.</param>
/// <param name="Stack">Its stack trace, when the browser gave one.</param>
/// <param name="Page">The app's path when it happened, such as /map.</param>
/// <param name="Source">Where it came from: a script URL with line and column, or the Vue component.</param>
public record ClientErrorReport(string? Message, string? Stack, string? Page, string? Source);
