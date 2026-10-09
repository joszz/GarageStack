using System.Collections.Concurrent;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Worker.Services;

/// <summary>
/// Runs climate schedules when they are due. Each due schedule is claimed first (its next run
/// moved on, or a one-off switched off), so a Worker that restarts mid-run never runs it twice. A
/// run takes up to a few minutes (each command up to ~30 s), so it goes on in the background and
/// the next look for due schedules is not held up; one car runs one schedule at a time.
/// </summary>
public sealed class ClimateScheduleService(
    ILogger<ClimateScheduleService> logger,
    IServiceScopeFactory scopeFactory,
    ClimateScheduleRunner runner,
    TimeProvider time) : PeriodicBackgroundService(logger)
{
    /// <summary>
    /// A run found this late is recorded as missed rather than started: the Worker was down when it
    /// was due, and a car warmed long after the driver left is no use.
    /// </summary>
    internal static readonly TimeSpan MissedAfter = TimeSpan.FromMinutes(10);

    // Vehicles with a run in progress. A schedule due while its car is busy waits for the next pass.
    private readonly ConcurrentDictionary<int, byte> _busyVehicles = new();
    private bool _recoveredInterruptedRuns;

    protected override string Name => "Climate schedules";

    // A schedule starts within this long of its time.
    protected override TimeSpan Interval => TimeSpan.FromSeconds(15);

    // Lets the MQTT connection come up first, so the answer to a run's first command is not missed.
    protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(30);

    /// <summary>The runs started by the last pass, for tests to wait on.</summary>
    internal IReadOnlyList<Task> StartedRuns { get; private set; } = [];

    protected override Task RunOnceAsync(CancellationToken ct) => RunPassAsync(ct);

    internal async Task RunPassAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var schedules = scope.ServiceProvider.GetRequiredService<IClimateScheduleRepository>();

        if (!_recoveredInterruptedRuns)
        {
            await schedules.MarkInterruptedAsync(ct);
            _recoveredInterruptedRuns = true;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var started = new List<Task>();
        foreach (var due in await schedules.GetDueAsync(now, ct))
        {
            if (!_busyVehicles.TryAdd(due.VehicleId, 0)) continue;

            var dueAt = DateTime.SpecifyKind(due.NextRunUtc!.Value, DateTimeKind.Utc);
            var missed = now - dueAt > MissedAfter;
            var claimed = await schedules.TryClaimAsync(due, s => Claim(s, now, dueAt, missed, ZoneOf(s)), ct);

            if (!claimed || missed)
            {
                _busyVehicles.TryRemove(due.VehicleId, out _);
                if (claimed)
                    logger.LogWarning("Climate schedule {ScheduleId} was due at {DueAt:u} and is missed", due.Id, dueAt);
                continue;
            }

            started.Add(RunInBackground(due, ct));
        }

        StartedRuns = started;
    }

    /// <summary>
    /// Moves a due schedule on: a repeating one to its next run after this one, a one-off off.
    /// Its last run starts as running, or as missed.
    /// </summary>
    internal static void Claim(ClimateSchedule schedule, DateTime nowUtc, DateTime dueAtUtc, bool missed, TimeZoneInfo zone)
    {
        if (schedule.Days == ClimateScheduleDays.None)
        {
            schedule.Enabled = false;
            schedule.NextRunUtc = null;
        }
        else
        {
            var after = nowUtc > dueAtUtc ? nowUtc : dueAtUtc;
            schedule.NextRunUtc = ClimateScheduleCalendar.NextRunUtc(schedule.StartTime, schedule.Days, zone, after);
        }

        schedule.LastRunAt = missed ? dueAtUtc : nowUtc;
        schedule.LastRunOutcome = missed ? ClimateScheduleOutcome.Missed : ClimateScheduleOutcome.Running;
        schedule.LastRunFailedCommand = null;
        schedule.LastRunDetail = null;
    }

    // The API only saves zones it knows, so this falls back only if the host lost its zone data.
    private TimeZoneInfo ZoneOf(ClimateSchedule schedule)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(schedule.TimeZoneId, out var zone)) return zone;

        logger.LogWarning("Time zone {TimeZone} of climate schedule {ScheduleId} is unknown on this host; using UTC",
            schedule.TimeZoneId, schedule.Id);
        return TimeZoneInfo.Utc;
    }

    private async Task RunInBackground(ClimateSchedule schedule, CancellationToken ct)
    {
        // Off the pass that found it, so its commands wait without holding up other cars' schedules.
        await Task.Yield();
        try
        {
            await runner.RunAsync(schedule, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down: the next start marks the run unconfirmed.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Climate schedule {ScheduleId} run failed", schedule.Id);
            await MarkUnconfirmedAsync(schedule);
        }
        finally
        {
            _busyVehicles.TryRemove(schedule.VehicleId, out _);
        }
    }

    // So the page does not show the run as still going until the next Worker start.
    private async Task MarkUnconfirmedAsync(ClimateSchedule schedule)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var schedules = scope.ServiceProvider.GetRequiredService<IClimateScheduleRepository>();
            await schedules.UpdateAsync(schedule.VehicleId, schedule.Id, s =>
            {
                if (s.LastRunOutcome == ClimateScheduleOutcome.Running)
                    s.LastRunOutcome = ClimateScheduleOutcome.Unconfirmed;
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not mark climate schedule {ScheduleId}'s failed run as unconfirmed", schedule.Id);
        }
    }
}
