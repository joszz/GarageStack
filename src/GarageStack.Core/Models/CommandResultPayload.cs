using System.Text.Json;

namespace GarageStack.Core.Models;

/// <summary>
/// The JSON carried on the PostgreSQL command_result channel: written by the Worker when the
/// gateway answers a command, read by the Api's TelemetryNotificationService, which releases the
/// command gate and forwards the answer to browsers over SignalR. One type on both ends so the two
/// processes cannot disagree about the shape. <c>Topic</c> is the command's gateway topic without
/// its /set or /result suffix (e.g. doors/locked); <c>Detail</c> is the gateway's reason for a
/// failure, or null on success.
/// </summary>
public sealed record CommandResultPayload(int VehicleId, string Vin, string Topic, bool Success, string? Detail)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static CommandResultPayload? FromJson(string json) =>
        JsonSerializer.Deserialize<CommandResultPayload>(json, JsonOptions);
}
