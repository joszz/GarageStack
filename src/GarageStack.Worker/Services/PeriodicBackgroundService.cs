using GarageStack.Core.Helpers;

namespace GarageStack.Worker.Services;

/// <summary>
/// A Worker job that runs on a fixed interval: first after <see cref="InitialDelay"/>, then every
/// <see cref="Interval"/>. A failed run is logged and the next one goes ahead as planned, so one
/// bad pass (the database restarting, an upstream outage) never stops the job for good. Runs that
/// keep failing are logged like a dropped connection: the first in full, then a reminder every
/// <see cref="OutageReminder"/>, and the recovery.
/// </summary>
public abstract class PeriodicBackgroundService(ILogger logger) : BackgroundService
{
    internal static readonly TimeSpan OutageReminder = TimeSpan.FromMinutes(5);

    /// <summary>Names the job in its log lines.</summary>
    protected abstract string Name { get; }

    protected abstract TimeSpan Interval { get; }

    /// <summary>How long after start the first run waits; zero runs it straight away.</summary>
    protected virtual TimeSpan InitialDelay => TimeSpan.Zero;

    /// <summary>One pass of the job.</summary>
    protected abstract Task RunOnceAsync(CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("{Service} started", Name);
        var outage = new OutageLog(logger, $"{Name} run", OutageReminder);

        try
        {
            if (InitialDelay > TimeSpan.Zero)
                await Task.Delay(InitialDelay, stoppingToken);

            using var timer = new PeriodicTimer(Interval);
            do
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                    outage.Recovered();
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    outage.Failed(ex);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
