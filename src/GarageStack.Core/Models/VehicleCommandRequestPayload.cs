using System.Text.Json;

namespace GarageStack.Core.Models;

/// <summary>
/// The JSON carried on the PostgreSQL vehicle_command_requested channel: written by the Worker when
/// a climate schedule needs a command sent, read by the Api, the only process that publishes
/// commands. <c>Command</c> is a VehicleCommands name (e.g. climate); <c>RequestId</c> only ties the
/// two processes' log lines together, since the gateway's answer names no request.
/// </summary>
public sealed record VehicleCommandRequestPayload(string RequestId, string Vin, string Command, string Value)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static VehicleCommandRequestPayload? FromJson(string json) =>
        JsonSerializer.Deserialize<VehicleCommandRequestPayload>(json, JsonOptions);
}
