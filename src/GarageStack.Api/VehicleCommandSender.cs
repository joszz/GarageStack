using GarageStack.Api.Endpoints;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Api;

/// <summary>
/// Sends one command to a vehicle: names the gateway topic, checks the value the gateway would
/// take, and publishes through <see cref="VehicleCommandGate"/> so it never reaches the gateway
/// while another command for the same car is still in flight. The command endpoint and the
/// Worker's command requests (climate schedules) both go through here, so a command is refused
/// and routed the same way whoever sends it.
/// </summary>
internal sealed class VehicleCommandSender(IMqttPublisher mqtt, VehicleCommandGate gate)
{
    internal const string AccountUnknownCode = "vehicle.accountUnknown";

    internal async Task<CommandSendOutcome> SendAsync(Vehicle vehicle, string command, string value, CancellationToken ct)
    {
        // The account arrives with the vehicle's first telemetry; until then the gateway topic a
        // command travels on cannot be named.
        if (vehicle.SaicUser is null)
            return CommandSendOutcome.Refused(AccountUnknownCode, "SAIC username not yet known for this vehicle");

        var commandTopic = VehicleCommands.TopicFor(command);
        if (commandTopic is null)
            return CommandSendOutcome.Refused("command.unknown", $"Unknown command '{command}'");

        if (VehicleCommands.Validate(command, value) is { } message)
            return CommandSendOutcome.Refused("command.invalidValue", message);

        var topic = $"saic/{vehicle.SaicUser}/vehicles/{vehicle.Vin}/{commandTopic}/set";
        await gate.RunAsync(vehicle.Vin, commandTopic, () => mqtt.PublishAsync(topic, value, ct), ct);
        return new CommandSendOutcome(topic, null);
    }
}

/// <summary>
/// What became of a command: the full topic it was published on, or why it was refused before
/// anything was published.
/// </summary>
internal readonly record struct CommandSendOutcome(string? Topic, ValidationError? Refusal)
{
    internal static CommandSendOutcome Refused(string code, string message) => new(null, new ValidationError(code, message));
}
