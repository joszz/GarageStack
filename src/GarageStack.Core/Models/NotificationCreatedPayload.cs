using System.Text.Json;
using System.Text.Json.Serialization;

namespace GarageStack.Core.Models;

/// <summary>
/// The JSON carried on the PostgreSQL notification_created channel: written by the Worker's
/// PushSenderService right after it stores an <see cref="AppNotification"/>, read by the Api's
/// TelemetryNotificationService and forwarded to browsers over SignalR. One type on both ends so
/// the two processes cannot disagree about the shape.
/// </summary>
public sealed record NotificationCreatedPayload(
    int Id,
    string Title,
    string Body,
    DateTime CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? VehicleId,
    int? UnreadCount)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static NotificationCreatedPayload From(AppNotification notification, int? unreadCount) => new(
        notification.Id,
        notification.Title,
        notification.Body,
        notification.CreatedAt,
        notification.Category,
        notification.VehicleId,
        unreadCount);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static NotificationCreatedPayload? FromJson(string json) =>
        JsonSerializer.Deserialize<NotificationCreatedPayload>(json, JsonOptions);
}
