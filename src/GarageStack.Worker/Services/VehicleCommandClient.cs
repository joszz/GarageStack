using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Extensions;
using GarageStack.Worker.Mqtt;

namespace GarageStack.Worker.Services;

/// <summary>What became of one command: whether the gateway answered, and if so how.</summary>
public readonly record struct CommandAnswer(bool Answered, bool Success, string? Detail)
{
    public static CommandAnswer NoAnswer => new(false, false, null);
}

/// <summary>Sends a vehicle command from the Worker and waits for the car's answer.</summary>
public interface IVehicleCommandClient
{
    Task<CommandAnswer> SendAsync(string vin, string command, string value, CancellationToken ct);
}

/// <summary>
/// Asks the Api to send the command (only the Api publishes, through its one-at-a-time gate) and
/// waits for the gateway's answer, which reaches this process first. A request the Api never gets,
/// because it is down or reconnecting to Postgres, ends as no answer: better than a climate start
/// that arrives long after it was due.
/// </summary>
public sealed class VehicleCommandClient(
    IServiceScopeFactory scopeFactory,
    CommandAnswerWaiter answers,
    ILogger<VehicleCommandClient> logger) : IVehicleCommandClient
{
    /// <summary>
    /// The Api's deadline for getting the command through its gate (90 s), plus the ~30 s the
    /// gateway can take to answer, plus margin: a late answer is never counted as none.
    /// </summary>
    internal static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(150);

    public async Task<CommandAnswer> SendAsync(string vin, string command, string value, CancellationToken ct)
    {
        var topic = VehicleCommands.TopicFor(command)
            ?? throw new ArgumentException($"Unknown command '{command}'", nameof(command));

        using var pending = answers.Expect(vin, topic);

        var request = new VehicleCommandRequestPayload(Guid.NewGuid().ToString("N")[..8], vin, command, value);
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.NotifyAsync(PgChannels.VehicleCommandRequested, request.ToJson(), ct);
        }
        logger.LogInformation("Asked the Api to send {Command} ({RequestId})", command, request.RequestId);

        var result = await pending.WaitAsync(AnswerTimeout, ct);
        if (result is not { } answer)
        {
            logger.LogWarning("No answer to {Command} ({RequestId}) within {Seconds}s",
                command, request.RequestId, AnswerTimeout.TotalSeconds);
            return CommandAnswer.NoAnswer;
        }

        return new CommandAnswer(true, answer.Success, answer.Detail);
    }
}
