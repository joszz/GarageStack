using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Worker.Services;

public class PhoneFusionService(
    ILogger<PhoneFusionService> logger, IServiceScopeFactory scopes, DawarichOptions options,
    TimeProvider time) : PeriodicBackgroundService(logger)
{
    public const int BatchSize = 10;
    /// <summary>Passes a trip may fail while Dawarich itself answers, before it is marked failed.</summary>
    public const int MaxTripAttempts = 5;
    private DateTimeOffset _retryAt;
    private bool _badKey;
    // Failed passes per trip while Dawarich was up. Kept in memory: a restart only gives a trip
    // a few more tries.
    private readonly Dictionary<long, int> _attempts = [];
    protected override string Name => "Dawarich phone fusion";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(3);
    protected override TimeSpan InitialDelay => TimeSpan.FromMinutes(1);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        options.Enabled ? base.ExecuteAsync(stoppingToken) : Task.CompletedTask;

    protected override Task RunOnceAsync(CancellationToken ct) => RunPassAsync(ct);

    public async Task RunPassAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (!options.Enabled || now < _retryAt) return;
        using var scope = scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPhoneFusionRepository>();
        var client = scope.ServiceProvider.GetRequiredService<IDawarichClient>();
        await repo.QueueBackfillAsync(ct);
        foreach (var id in await repo.PendingAsync(now.UtcDateTime.AddMinutes(-options.FusionDelayMinutes), BatchSize, ct))
        {
            var work = await repo.ReadWorkAsync(id, ct);
            if (work is null) continue;
            try
            {
                var from = work.Parked?.RecordedAt ?? work.Points[0].RecordedAt;
                if (work.PreviousEnd is { } previous && from < previous) from = previous;
                var fixes = await client.GetPointsAsync(from, work.Points[^1].RecordedAt, ct);
                var result = TripPhoneFusion.Apply(work.Points, work.Parked, work.PreviousEnd,
                    fixes.Where(f => work.Trackers.Any(t => t.TrackerId == f.TrackerId)).ToArray());
                var applied = await repo.CompleteAsync(work, result, now.UtcDateTime, ct);
                if (_badKey) logger.LogInformation("Dawarich authentication recovered");
                _badKey = false;
                _attempts.Remove(id);
                if (applied is { } outcome)
                    logger.LogInformation("Phone fusion trip {TripId}: {Outcome}, {Added} points added",
                        id, outcome, outcome == PhoneFusionOutcome.Fused ? result.Points.Count - work.Points.Count : 0);
                else
                    logger.LogInformation("Phone fusion trip {TripId} skipped after a concurrent change", id);
            }
            catch (DawarichException ex) when (ex.BadKey)
            {
                if (!_badKey) logger.LogWarning("Dawarich rejected its API key; phone fusion is paused");
                _badKey = true;
                _retryAt = now.AddMinutes(30);
                return;
            }
            catch (DawarichException ex) when (!ex.Retryable)
            {
                // An answer Dawarich will give again: this trip fails, and the next one need not wait.
                logger.LogWarning("Phone fusion trip {TripId}: {Reason}", id, ex.Message);
                await FailAsync(repo, work, now, ct);
            }
            catch (DawarichException ex)
            {
                // When Dawarich answers its health check, the trouble lies in this trip's window:
                // count it against the trip and carry on, so one trip cannot hold up the rest.
                if (await client.CheckAsync(ct) == DawarichHealth.Ok)
                {
                    var attempts = _attempts.GetValueOrDefault(id) + 1;
                    logger.LogWarning("Phone fusion trip {TripId}: {Reason} (attempt {Attempt} of {Max})",
                        id, ex.Message, attempts, MaxTripAttempts);
                    if (attempts < MaxTripAttempts) _attempts[id] = attempts;
                    else await FailAsync(repo, work, now, ct);
                    continue;
                }
                logger.LogWarning("Phone fusion paused: {Reason}", ex.Message);
                _retryAt = now.AddMinutes(5);
                return;
            }
        }
    }

    private async Task FailAsync(IPhoneFusionRepository repo, PhoneFusionWork work, DateTimeOffset now, CancellationToken ct)
    {
        _attempts.Remove(work.TripId);
        await repo.CompleteAsync(work, new(work.Points, PhoneFusionOutcome.Failed), now.UtcDateTime, ct);
    }
}
