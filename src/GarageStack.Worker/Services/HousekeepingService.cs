using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;

namespace GarageStack.Worker.Services;

/// <summary>
/// Keeps the database from growing without end. Compacts each vehicle's old telemetry with
/// <see cref="TelemetryCompactor"/>, and removes cached place names and snapped trips that have
/// expired, and notifications the user deleted a while ago. Also works out the speed figures of
/// trips saved before those were stored with the trip. Cached map POIs stay: the map shows an
/// expired tile's stations while it fetches the tile again.
/// </summary>
public class HousekeepingService(
    ILogger<HousekeepingService> logger,
    IServiceScopeFactory scopeFactory,
    TelemetryRetentionOptions retention) : PeriodicBackgroundService(logger)
{
    // Nothing reads an expired answer, but a lookup may be refreshing it at this very moment.
    // A day's grace keeps out of its way.
    internal static readonly TimeSpan ExpiredCacheGrace = TimeSpan.FromDays(1);

    // The notification cooldowns read the history, deleted rows included, at most a week back.
    internal static readonly TimeSpan DeletedNotificationsKept = TimeSpan.FromDays(30);

    // Trips per save while filling in speed figures: each one reads its fixes.
    internal const int SpeedFigureBatch = 200;

    protected override string Name => "Housekeeping";

    // After the trip recorder's first run, whose line compaction waits for.
    protected override TimeSpan InitialDelay => TimeSpan.FromMinutes(10);

    protected override TimeSpan Interval => TimeSpan.FromHours(6);

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var housekeeping = scope.ServiceProvider.GetRequiredService<IHousekeepingRepository>();
        var vehicles = await scope.ServiceProvider.GetRequiredService<IVehicleRepository>().GetAllAsync(ct);
        var compactor = new TelemetryCompactor(housekeeping, retention);
        var now = DateTime.UtcNow;

        foreach (var vehicle in vehicles)
        {
            var folded = await compactor.CompactAsync(vehicle, now, ct);
            if (folded > 0)
                logger.LogInformation("Compacted old telemetry for VIN={Vin}: {Count} row(s) folded into quarter-hour rows",
                    LogRedaction.Vin(vehicle.Vin), folded);
        }

        var trips = scope.ServiceProvider.GetRequiredService<ITripRepository>();
        int filled, totalFilled = 0;
        do
        {
            filled = await trips.FillSpeedFiguresAsync(SpeedFigureBatch, ct);
            totalFilled += filled;
        }
        while (filled == SpeedFigureBatch);
        if (totalFilled > 0)
            logger.LogInformation("Worked out speed figures for {Count} trip(s) saved before they were stored", totalFilled);

        var expired = await housekeeping.RemoveExpiredCacheEntriesAsync(now - ExpiredCacheGrace, ct);
        var deleted = await housekeeping.RemoveDeletedNotificationsAsync(now - DeletedNotificationsKept, ct);
        if (expired > 0 || deleted > 0)
            logger.LogInformation("Housekeeping removed {Expired} expired cache entries and {Deleted} deleted notifications", expired, deleted);
    }
}
