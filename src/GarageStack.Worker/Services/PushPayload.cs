using System.Text.Json;
using GarageStack.Core.Models;
using Microsoft.Extensions.Localization;

namespace GarageStack.Worker.Services;

/// <summary>
/// The JSON a push message carries to the service worker (frontend/src/sw.ts), which shows it.
/// Besides the text it names the buttons the notification offers (<see cref="NotificationActions"/>)
/// with their labels, and what the worker needs to carry one out: the car's VIN, and the
/// maintenance item for "mark as done". Push messages are encrypted for the subscribed browser, so
/// the push service relaying them reads none of it.
/// </summary>
internal static class PushPayload
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>A button: what it does, its label, and what the notification says once it was done.</summary>
    internal sealed record Action(string Name, string Title, string Sent);

    internal sealed record Message(
        string Title,
        string Body,
        string? Category,
        int? UnreadCount,
        string? Vin,
        int? MaintenanceItemId,
        IReadOnlyList<Action>? Actions);

    /// <summary>
    /// The message for a notification. A category with actions gets them only when the car is
    /// known: every action is sent to one car.
    /// </summary>
    public static Message For(
        string title, string body, string? category, int? unreadCount, string? vin,
        IStringLocalizer<NotificationStrings> strings)
    {
        var actions = vin is null
            ? []
            : NotificationActions.For(category).Select(name => Describe(name, strings)).ToList();
        return actions.Count == 0
            ? new Message(title, body, category, unreadCount, null, null, null)
            : new Message(title, body, category, unreadCount, vin, NotificationCategories.MaintenanceItemId(category), actions);
    }

    public static string Serialize(Message message) => JsonSerializer.Serialize(message, Options);

    private static Action Describe(string name, IStringLocalizer<NotificationStrings> strings) => name switch
    {
        NotificationActions.Lock => new(name, strings["ActionLockTitle"], strings["ActionLockSent"]),
        NotificationActions.MaintenanceDone => new(name, strings["ActionMaintenanceDoneTitle"], strings["ActionMaintenanceDoneSent"]),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No label for this notification action"),
    };
}
