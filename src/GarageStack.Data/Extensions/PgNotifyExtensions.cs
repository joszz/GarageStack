using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GarageStack.Data.Extensions;

/// <summary>
/// Channel names for the PostgreSQL pub/sub bridge between the Worker (publisher) and the Api
/// (LISTENer, see TelemetryNotificationService). Declared once so a typo cannot silently split
/// the two sides.
/// </summary>
public static class PgChannels
{
    public const string TelemetryUpdated = "telemetry_updated";
    public const string NotificationCreated = "notification_created";
    public const string TripCompleted = "trip_completed";
    public const string CommandResult = "command_result";

    public static readonly IReadOnlyList<string> All = [TelemetryUpdated, NotificationCreated, TripCompleted, CommandResult];
}

public static class PgNotifyExtensions
{
    /// <summary>
    /// Raises a pg_notify on <paramref name="channel"/>. A no-op on non-relational providers
    /// (the in-memory database used by demo mode and the tests), so callers never need their
    /// own provider check.
    /// </summary>
    public static Task NotifyAsync(this DatabaseFacade database, string channel, string payload, CancellationToken ct)
    {
        if (!database.IsRelational()) return Task.CompletedTask;
        return database.ExecuteSqlAsync($"SELECT pg_notify({channel}, {payload})", ct);
    }
}
