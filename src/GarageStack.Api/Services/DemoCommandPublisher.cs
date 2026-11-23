using GarageStack.Api.Hubs;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Data.Demo;
using Microsoft.AspNetCore.SignalR;

namespace GarageStack.Api.Services;

/// <summary>
/// Demo mode's gateway. Instead of publishing a command it carries it out on the demo car it names a
/// moment later and answers it the way the gateway would: the car's new state goes out as telemetry
/// to that car's viewers, the answer as a command result, and the command gate is released. The demo
/// then shows a command's whole round trip, the button waiting and the card changing, instead of a
/// wait that times out.
/// </summary>
internal sealed class DemoCommandPublisher(
    DemoTelemetryRepository car,
    DemoOptions demo,
    IHubContext<TelemetryHub> hub,
    VehicleCommandGate gate,
    ILogger<DemoCommandPublisher> logger,
    TimeSpan? answerDelay = null) : IMqttPublisher
{
    // Long enough to see the button wait, far shorter than a real car's 5 to 30 seconds.
    private readonly TimeSpan _answerDelay = answerDelay ?? TimeSpan.FromSeconds(2);

    public Task PublishAsync(string topic, string payload, CancellationToken ct = default)
    {
        // saic/{user}/vehicles/{vin}/{command topic}/set, as VehicleCommandSender builds it.
        var parts = topic.Split('/');
        if (parts.Length < 6 || parts[^1] != "set") return Task.CompletedTask;
        var vin = parts[3];
        var commandTopic = string.Join('/', parts[4..^1]);
        if (VehicleCommands.CommandFor(commandTopic) is not { } command) return Task.CompletedTask;

        // Answered in the background, as the gateway does: the request that sent the command
        // returns at once and the browser waits for the answer.
        _ = AnswerAsync(vin, commandTopic, command, payload);
        return Task.CompletedTask;
    }

    private async Task AnswerAsync(string vin, string commandTopic, string command, string value)
    {
        try
        {
            await Task.Delay(_answerDelay);
            // Only the demo's own cars are sent commands: the API finds the car before it sends one.
            if (demo.Vehicles.FirstOrDefault(v => v.Vin == vin) is not { } vehicle) return;

            var refusal = car.ApplyCommand(vehicle.Id, command, value);
            var group = hub.Clients.Group(TelemetryHub.GroupFor(vehicle.Id));

            if (refusal is null && await car.GetMergedLatestAsync(vehicle.Id) is { } snapshot)
                await group.SendAsync("telemetryUpdated", snapshot);
            await group.SendAsync("commandResult", new CommandResultMessage(command, refusal is null, refusal));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Demo mode could not carry out the {Command} command", command);
        }
        finally
        {
            gate.Complete(vin, commandTopic);
        }
    }
}
