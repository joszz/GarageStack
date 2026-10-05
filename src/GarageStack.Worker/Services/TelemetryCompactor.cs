using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Worker.Services;

/// <summary>
/// Compacts a vehicle's telemetry once it is older than the full-detail period, a whole UTC day at
/// a time, from its compaction line onwards. It never passes the trip recording line: the trip
/// recorder cuts trips from every fix after that line, and a quarter-hour row has lost the fixes in
/// between. A vehicle whose trips have never been recorded is left alone for the same reason.
/// </summary>
public class TelemetryCompactor(IHousekeepingRepository housekeeping, TelemetryRetentionOptions options)
{
    /// <returns>How many rows were folded away.</returns>
    public async Task<int> CompactAsync(Vehicle vehicle, DateTime now, CancellationToken ct)
    {
        if (options.FullDetailDays is not { } days || vehicle.TripsRecordedUntil is not { } recordedUntil)
            return 0;

        var fullDetailFrom = now.AddDays(-days);
        var until = (recordedUntil < fullDetailFrom ? recordedUntil : fullDetailFrom).Date;
        var from = vehicle.TelemetryCompactedUntil
                   ?? (await housekeeping.GetFirstTelemetryAtAsync(vehicle.Id, ct))?.Date;
        if (from is null) return 0;

        var folded = 0;
        for (var day = from.Value; day < until; day = day.AddDays(1))
            folded += await housekeeping.CompactTelemetryAsync(vehicle.Id, day, day.AddDays(1), ct);

        return folded;
    }
}
