using GarageStack.Core.Models;

namespace GarageStack.Api.Hubs;

/// <summary>
/// The gateway's answer to a command as browsers receive it on "commandResult": named by the
/// command the browser sent (e.g. "lock") rather than by the gateway topic it travelled on.
/// </summary>
public sealed record CommandResultMessage(string Command, bool Success, string? Detail)
{
    /// <returns>null when the answer is for a command the API does not send, which no browser is waiting on.</returns>
    internal static CommandResultMessage? From(CommandResultPayload payload) =>
        VehicleCommands.CommandFor(payload.Topic) is { } command
            ? new CommandResultMessage(command, payload.Success, payload.Detail)
            : null;
}
