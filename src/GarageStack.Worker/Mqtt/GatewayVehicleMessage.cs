using System.Globalization;
using System.Text.Json;
using GarageStack.Core.Models;

namespace GarageStack.Worker.Mqtt;

/// <summary>
/// A message the official MG app shows, as the gateway's events/vehicleMessage event carries it
/// (saic-python-mqtt-gateway, status_publisher/message.py). The event holds the text but no id or
/// time: the gateway publishes those just before it, on the retained info/lastMessage/messageId and
/// info/lastMessage/messageTime topics, which <see cref="ParseId"/> and <see cref="ParseSentAt"/> read.
/// <c>MessageType</c> is SAIC's numeric type code as a string, empty when it gave none.
/// </summary>
public readonly record struct GatewayVehicleMessage(string Title, string Content, string MessageType)
{
    public const string EventSubtopic = "events/vehicleMessage";
    public const string IdSubtopic = "info/lastMessage/messageId";
    public const string SentAtSubtopic = "info/lastMessage/messageTime";

    // Both end up in a web push, whose encrypted payload must stay under about 4 KB, and on a phone
    // screen. A real message is a sentence or two.
    internal const int MaxTitleLength = 100;
    internal const int MaxContentLength = 500;

    /// <returns>false when <paramref name="payload"/> is not a JSON object.</returns>
    public static bool TryParse(string payload, out GatewayVehicleMessage message)
    {
        message = default;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            message = new GatewayVehicleMessage(
                Clip(ReadString(root, "title"), MaxTitleLength),
                Clip(ReadString(root, "content"), MaxContentLength),
                ReadString(root, "message_type"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <returns>The message id, or null when the payload is empty or too long to be one.</returns>
    public static string? ParseId(string payload)
    {
        var id = payload.Trim();
        return id.Length is > 0 and <= VehicleLimits.MessageIdMaxLength ? id : null;
    }

    /// <returns>
    /// When SAIC says the message was sent, or null when unreadable. The gateway writes ISO 8601 in
    /// UTC, but SAIC's own timestamps carry no zone and the gateway assumes UTC for them, so the
    /// answer can be off by the account's UTC offset.
    /// </returns>
    public static DateTimeOffset? ParseSentAt(string payload) =>
        DateTimeOffset.TryParse(payload, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var sentAt)
            ? sentAt
            : null;

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!.Trim()
            : string.Empty;

    private static string Clip(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength].TrimEnd();
}
