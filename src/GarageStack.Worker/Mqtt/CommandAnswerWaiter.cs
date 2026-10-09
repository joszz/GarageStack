using System.Collections.Concurrent;

namespace GarageStack.Worker.Mqtt;

/// <summary>
/// Gateway answers the Worker itself is waiting for: a climate schedule asks the Api to send a
/// command and waits here for the answer on that command's topic. <see cref="CommandResultHandler"/>
/// hands in every live answer; one nobody waits for is simply passed over. The answer names a
/// command but not a request, so an answer to the same command sent by hand at the same moment
/// counts as this one's too.
/// </summary>
public sealed class CommandAnswerWaiter
{
    private readonly ConcurrentDictionary<(string Vin, string Topic), TaskCompletionSource<GatewayCommandResult>> _waiting = new();

    /// <summary>
    /// Starts waiting for the next answer on <paramref name="topic"/> for <paramref name="vin"/>.
    /// Called before the command is sent, so even an instant answer is caught.
    /// </summary>
    public PendingAnswer Expect(string vin, string topic)
    {
        // Continuations run off the MQTT receive path, so a waiting run never holds up the next message.
        var answer = new TaskCompletionSource<GatewayCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting[(vin, topic)] = answer;
        return new PendingAnswer(this, (vin, topic), answer);
    }

    public void Complete(string vin, GatewayCommandResult result)
    {
        if (_waiting.TryRemove((vin, result.Topic), out var answer))
            answer.TrySetResult(result);
    }

    private void Forget((string Vin, string Topic) key, TaskCompletionSource<GatewayCommandResult> answer) =>
        _waiting.TryRemove(new KeyValuePair<(string, string), TaskCompletionSource<GatewayCommandResult>>(key, answer));

    /// <summary>One wait for an answer; disposing it stops waiting.</summary>
    public sealed class PendingAnswer : IDisposable
    {
        private readonly CommandAnswerWaiter _owner;
        private readonly (string Vin, string Topic) _key;
        private readonly TaskCompletionSource<GatewayCommandResult> _answer;

        internal PendingAnswer(CommandAnswerWaiter owner, (string Vin, string Topic) key, TaskCompletionSource<GatewayCommandResult> answer)
        {
            _owner = owner;
            _key = key;
            _answer = answer;
        }

        /// <returns>The answer, or null when none came within <paramref name="timeout"/>.</returns>
        public async Task<GatewayCommandResult?> WaitAsync(TimeSpan timeout, CancellationToken ct)
        {
            try
            {
                return await _answer.Task.WaitAsync(timeout, ct);
            }
            catch (TimeoutException)
            {
                return null;
            }
        }

        public void Dispose() => _owner.Forget(_key, _answer);
    }
}
