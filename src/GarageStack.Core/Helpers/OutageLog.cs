using Microsoft.Extensions.Logging;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Logs a connection that keeps failing and retrying (the broker, the database) without filling
/// the log files: the first failure in full, a reminder now and then while it lasts, and the
/// recovery. Every retry in between goes to Debug. One instance per connection loop.
/// </summary>
public sealed class OutageLog(ILogger logger, string what, TimeSpan remindEvery, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private DateTimeOffset? _since;
    private DateTimeOffset _lastReminder;
    private int _attempts;

    public void Failed(Exception error)
    {
        var now = _time.GetUtcNow();
        _attempts++;

        if (_since is null)
        {
            _since = _lastReminder = now;
            logger.LogError(error, "{What} failed; retrying, and only logging it again every {Interval} until it recovers", what, remindEvery);
        }
        else if (now - _lastReminder >= remindEvery)
        {
            _lastReminder = now;
            logger.LogWarning("{What} still failing after {Attempts} attempts over {Duration}: {Reason}", what, _attempts, now - _since, error.Message);
        }
        else
        {
            logger.LogDebug(error, "{What} failed again (attempt {Attempts})", what, _attempts);
        }
    }

    /// <summary>Called on every success; only the first after an outage logs anything.</summary>
    public void Recovered()
    {
        if (_since is not { } since) return;

        logger.LogInformation("{What} recovered after {Duration}", what, _time.GetUtcNow() - since);
        _since = null;
        _attempts = 0;
    }
}
