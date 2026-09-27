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
    /// <summary>Push services want a way to reach whoever sends; used when the deployment names none.</summary>
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
        Subject = configuration.TextOrDefault("Vapid:Subject", FallbackSubject),
    };
}
