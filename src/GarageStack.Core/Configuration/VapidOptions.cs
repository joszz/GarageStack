using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;

namespace GarageStack.Core.Configuration;

/// <summary>
/// The Web Push identity (VAPID_PUBLIC_KEY, VAPID_PRIVATE_KEY and a contact subject). The Worker
/// signs pushes with it and the API hands browsers the public half. Without both keys push stays
/// off and notifications only reach the in-app bell.
/// </summary>
public sealed class VapidOptions
{
    /// <summary>
    /// Push services want a way to reach whoever sends. Used when the deployment names no contact
    /// and has no https address to offer instead.
    /// </summary>
    public const string FallbackSubject = "mailto:admin@garagestack.local";

    public string? PublicKey { get; init; }

    public string? PrivateKey { get; init; }

    public string Subject { get; init; } = FallbackSubject;

    [MemberNotNullWhen(true, nameof(PublicKey), nameof(PrivateKey))]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);

    public static VapidOptions From(IConfiguration configuration) => new()
    {
        PublicKey = configuration["Vapid:PublicKey"],
        PrivateKey = configuration["Vapid:PrivateKey"],
        Subject = configuration.TextOrDefault("Vapid:Subject", DefaultSubject(configuration)),
    };

    // The subject travels to the browser vendors' push services with every notification, so the
    // default is never personal: the address GarageStack is served on when that is https (a
    // valid VAPID subject, RFC 8292), otherwise a placeholder. It used to be the MG account email.
    private static string DefaultSubject(IConfiguration configuration) =>
        Uri.TryCreate(configuration["Cors:Origins:0"], UriKind.Absolute, out var origin) && origin.Scheme == Uri.UriSchemeHttps
            ? origin.GetLeftPart(UriPartial.Authority)
            : FallbackSubject;
}
