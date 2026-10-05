using System.Text.Json;
using GarageStack.Api.Authentication;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace GarageStack.Api.Endpoints;

/// <summary>
/// The signed-in account's settings, kept on the server so they follow it to every device. The
/// browser owns what the settings mean; the server keeps them per section and merges each save
/// key by key.
/// </summary>
public static class SettingsEndpoints
{
    /// <summary>A save carries a few changed keys; even a whole section is a fraction of this.</summary>
    internal const long MaxRequestBytes = 2L * UserSettingsLimits.JsonMaxLength;

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings")
            .WithTags("Settings")
            .RequireAuthorization();

        group.MapGet("/", async (HttpContext httpContext, IUserSettingsRepository settings, CancellationToken ct) =>
        {
            var account = SessionPrincipal.ResolveAccountKey(httpContext.User);
            if (account is null) return NoAccount();

            return Results.Ok(await settings.GetAsync(account, ct));
        })
        .WithSummary("Get the signed-in account's settings, by section; a section never saved is left out");

        group.MapPatch("/{section}", async (
            HttpContext httpContext,
            string section,
            [FromBody] JsonElement changes,
            IUserSettingsRepository settings,
            CancellationToken ct) =>
        {
            var account = SessionPrincipal.ResolveAccountKey(httpContext.User);
            if (account is null) return NoAccount();

            if (!UserSettingsLimits.Sections.Contains(section))
                return ApiProblems.Problem(StatusCodes.Status404NotFound, "settings.unknownSection", "There is no such settings section");

            var error = Validate(changes);
            if (error is not null) return ApiProblems.BadRequest(error);

            return await settings.MergeAsync(account, section, ToChanges(changes), ct) switch
            {
                SettingsSaveResult.TooLarge => ApiProblems.Problem(StatusCodes.Status413PayloadTooLarge, "settings.tooLarge",
                    $"A settings section may hold at most {UserSettingsLimits.JsonMaxLength} characters"),
                SettingsSaveResult.Conflict => ApiProblems.Problem(StatusCodes.Status409Conflict, "settings.conflict",
                    "Other devices were saving the same settings at the same moment; send the change again"),
                _ => Results.NoContent(),
            };
        })
        .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
        .WithSummary("Save some of a section's settings; the keys left out keep their saved values");

        return app;
    }

    /// <summary>Why a save is refused, or null when it may be merged.</summary>
    internal static ValidationError? Validate(JsonElement changes)
    {
        if (changes.ValueKind != JsonValueKind.Object)
            return new("settings.notAnObject", "Settings must be a JSON object of the keys to save");

        var count = 0;
        foreach (var property in changes.EnumerateObject())
        {
            if (++count > UserSettingsLimits.MaxKeys)
                return new("settings.tooManyKeys", $"A save may carry at most {UserSettingsLimits.MaxKeys} keys");
            if (!IsKey(property.Name))
                return new("settings.invalidKey", "Setting keys are letters and digits, starting with a letter");
        }

        return count == 0 ? new("settings.empty", "A save must carry at least one key") : null;
    }

    // The browser's own field names: camelCase identifiers. Nothing else is needed, and keeping
    // to them rules out keys such as "__proto__" before they ever reach a browser.
    private static bool IsKey(string name) =>
        name.Length is > 0 and <= UserSettingsLimits.KeyMaxLength
        && char.IsAsciiLetter(name[0])
        && name.All(char.IsAsciiLetterOrDigit);

    // A key sent twice counts once, the last time, as a JSON parser in the browser would read it.
    private static Dictionary<string, JsonElement> ToChanges(JsonElement changes)
    {
        var byKey = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in changes.EnumerateObject())
            byKey[property.Name] = property.Value;
        return byKey;
    }

    private static IResult NoAccount() =>
        ApiProblems.Problem(StatusCodes.Status403Forbidden, "settings.noAccount",
            "This session names no account to keep settings for");
}
