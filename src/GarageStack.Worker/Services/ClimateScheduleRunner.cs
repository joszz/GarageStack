using System.Globalization;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace GarageStack.Worker.Services;

/// <summary>
/// Carries out one run of a climate schedule that <see cref="ClimateScheduleService"/> has just
/// claimed: checks it should run, sends its commands one at a time (the car takes one at a time),
/// records how it went and tells the user.
/// </summary>
public sealed class ClimateScheduleRunner(
    IServiceScopeFactory scopeFactory,
    IVehicleCommandClient commands,
    IPushSender pushSender,
    IStringLocalizer<NotificationStrings> strings,
    TimeProvider time,
    ILogger<ClimateScheduleRunner> logger)
{
    /// <summary>
    /// How old an outside temperature may be and still decide a run. A parked car is polled
    /// rarely, and yesterday afternoon's reading says nothing about this morning.
    /// </summary>
    internal static readonly TimeSpan ExteriorReadingMaxAge = TimeSpan.FromHours(3);

    /// <summary>How a run went: its outcome, and the command that failed or went unanswered, if any.</summary>
    internal readonly record struct RunResult(ClimateScheduleOutcome Outcome, string? FailedCommand, string? Detail);

    /// <summary>Runs <paramref name="schedule"/>, as read when it was due, with its vehicle.</summary>
    public async Task RunAsync(ClimateSchedule schedule, CancellationToken ct)
    {
        var (engineRunning, exteriorC) = await ReadConditionsAsync(schedule.VehicleId, ct);
        if (ClimateScheduleConditions.SkipReason(schedule, engineRunning, exteriorC) is { } skipped)
        {
            logger.LogInformation("Climate schedule {ScheduleId} skipped: {Outcome}", schedule.Id, skipped);
            await RecordAsync(schedule, new RunResult(skipped, null, null), ct);
            return;
        }

        var result = await SendCommandsAsync(schedule, ct);
        logger.LogInformation("Climate schedule {ScheduleId} ran: {Outcome}", schedule.Id, result.Outcome);
        await RecordAsync(schedule, result, ct);
        await NotifyAsync(schedule, result, ct);
    }

    private async Task<(bool? EngineRunning, double? ExteriorC)> ReadConditionsAsync(int vehicleId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
        var latest = await telemetry.GetMergedLatestAsync(vehicleId, ct);

        // Not the merged snapshot's value, which can be any age: only a recent reading counts.
        var since = time.GetUtcNow().UtcDateTime - ExteriorReadingMaxAge;
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exteriorC = await db.TelemetrySnapshots.AsNoTracking()
            .Where(s => s.VehicleId == vehicleId && s.RecordedAt >= since && s.ExteriorTemperature != null)
            .OrderByDescending(s => s.RecordedAt)
            .Select(s => s.ExteriorTemperature)
            .FirstOrDefaultAsync(ct);

        return (latest?.EngineRunning, exteriorC);
    }

    // Stops at the first command the car refuses or does not answer: a car that is asleep or out
    // of reach takes none of the rest either.
    internal async Task<RunResult> SendCommandsAsync(ClimateSchedule schedule, CancellationToken ct)
    {
        var started = false;
        foreach (var step in ClimateSchedulePlan.For(schedule))
        {
            // Until the car's account is known, no command can be addressed to it.
            var answer = schedule.Vehicle.SaicUser is null
                ? new CommandAnswer(true, false, null)
                : await commands.SendAsync(schedule.Vehicle.Vin, step.Command, step.Value, ct);

            if (answer is { Answered: true, Success: true })
            {
                started |= step.Command == ClimateSchedulePlan.ClimateCommand;
                continue;
            }

            var outcome = started ? ClimateScheduleOutcome.Started
                : answer.Answered ? ClimateScheduleOutcome.Failed
                : ClimateScheduleOutcome.Unconfirmed;
            return new RunResult(outcome, step.Command, answer.Detail);
        }

        return new RunResult(ClimateScheduleOutcome.Started, null, null);
    }

    private async Task RecordAsync(ClimateSchedule schedule, RunResult result, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var schedules = scope.ServiceProvider.GetRequiredService<IClimateScheduleRepository>();
        await schedules.UpdateAsync(schedule.VehicleId, schedule.Id, s =>
        {
            s.LastRunOutcome = result.Outcome;
            s.LastRunFailedCommand = result.FailedCommand;
            s.LastRunDetail = result.Detail is { Length: > ClimateScheduleLimits.DetailMaxLength } tooLong
                ? tooLong[..ClimateScheduleLimits.DetailMaxLength]
                : result.Detail;
        }, ct);
    }

    private async Task NotifyAsync(ClimateSchedule schedule, RunResult result, CancellationToken ct)
    {
        if (BuildNotification(schedule, result, strings) is not { } notification) return;
        await pushSender.SendToAllAsync(notification.Title, notification.Body, ct, NotificationCategories.ClimateSchedule, schedule.VehicleId);
    }

    /// <summary>The push for a run, or null for one that ran nothing (skipped).</summary>
    internal static (string Title, string Body)? BuildNotification(
        ClimateSchedule schedule, RunResult result, IStringLocalizer<NotificationStrings> strings)
    {
        var time = schedule.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        var reference = schedule.Name is { } name
            ? strings["ClimateScheduleRefNamed", name, time].Value
            : strings["ClimateScheduleRef", time].Value;

        (string Title, string Body)? Text(string title, LocalizedString body) => (strings[title].Value, body.Value);

        return result.Outcome switch
        {
            ClimateScheduleOutcome.Started when result.FailedCommand is { } extra => Text(
                "ClimateScheduleStartedTitle",
                strings["ClimateScheduleStartedPartlyBody", reference, CommandName(extra, strings)]),
            ClimateScheduleOutcome.Started => Text(
                "ClimateScheduleStartedTitle",
                strings["ClimateScheduleStartedBody", reference]),
            ClimateScheduleOutcome.Failed => Text(
                "ClimateScheduleFailedTitle",
                result.Detail is { } detail
                    ? strings["ClimateScheduleFailedBodyWithDetail", reference, detail]
                    : strings["ClimateScheduleFailedBody", reference]),
            ClimateScheduleOutcome.Unconfirmed => Text(
                "ClimateScheduleNoAnswerTitle",
                strings["ClimateScheduleNoAnswerBody", reference]),
            _ => null,
        };
    }

    // Only the extras can fail after climate started, so only they need a name.
    private static string CommandName(string command, IStringLocalizer<NotificationStrings> strings) => strings[command switch
    {
        "rear-defroster" => "ClimateScheduleCommandRearDefroster",
        "seat-left" => "ClimateScheduleCommandSeatLeft",
        "seat-right" => "ClimateScheduleCommandSeatRight",
        _ => "ClimateScheduleCommandTemperature",
    }].Value;
}
