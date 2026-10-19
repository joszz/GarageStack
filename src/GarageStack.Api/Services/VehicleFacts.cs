using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Api.Services;

/// <summary>
/// What GarageStack knows about a car beyond its telemetry: the APK and recalls the RDW has, the
/// maintenance item due first, and the newest trip. The Homepage widget and Home Assistant both
/// read it from here, so the two say the same as the dashboard's cards.
/// </summary>
/// <param name="ApkExpiresOn">The APK's last valid day, while the RDW knows the plate.</param>
/// <param name="ApkDaysLeft">Days until then on the Dutch calendar, negative once it has run out.</param>
/// <param name="OpenRecalls">Recalls on the plate not reported repaired.</param>
/// <param name="WamInsured">Whether the RDW has WAM insurance on record.</param>
/// <param name="NextMaintenance">The name of the item due first: the most urgent, then the soonest.</param>
/// <param name="NextMaintenanceStatus">ok, dueSoon, overdue or unknown, as the maintenance page says it.</param>
/// <param name="NextMaintenanceDueDate">When that item is due by its time interval, if it has one.</param>
/// <param name="NextMaintenanceDaysLeft">Days until then, negative once it has passed.</param>
/// <param name="NextMaintenanceKmLeft">Kilometres until it is due by its distance interval, negative once passed.</param>
/// <param name="LastTripStartedAt">When the newest trip started.</param>
/// <param name="LastTripEndedAt">When it ended, or its latest fix while it is still being driven.</param>
/// <param name="LastTripDistanceKm">Its distance along the fixes.</param>
/// <param name="LastTripInProgress">The newest trip is the one being driven.</param>
public sealed record VehicleFacts(
    DateOnly? ApkExpiresOn,
    int? ApkDaysLeft,
    int? OpenRecalls,
    bool? WamInsured,
    string? NextMaintenance,
    string? NextMaintenanceStatus,
    DateTime? NextMaintenanceDueDate,
    int? NextMaintenanceDaysLeft,
    double? NextMaintenanceKmLeft,
    DateTime? LastTripStartedAt,
    DateTime? LastTripEndedAt,
    double? LastTripDistanceKm,
    bool? LastTripInProgress)
{
    public bool HasRdw => ApkExpiresOn is not null || OpenRecalls is not null || WamInsured is not null;

    public bool HasMaintenance => NextMaintenance is not null;

    public bool HasTrip => LastTripEndedAt is not null;
}

/// <summary>Builds <see cref="VehicleFacts"/> from the repositories, as of <see cref="TimeProvider"/>'s now.</summary>
public sealed class VehicleFactsService(
    IRdwRepository rdw,
    RdwOptions rdwOptions,
    IMaintenanceRepository maintenance,
    ITelemetryRepository telemetry,
    ITripRepository trips,
    TimeProvider time)
{
    public async Task<VehicleFacts> GetAsync(Vehicle vehicle, CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;

        var info = rdwOptions.Enabled && vehicle.LicensePlate is { } plate
            && await rdw.GetAsync(vehicle.Id, ct) is { Found: true } found && found.LicensePlate == plate
            ? found
            : null;

        var odometerKm = await telemetry.GetOdometerAtAsync(vehicle.Id, now, ct);
        var next = MostUrgent(await maintenance.ListAsync(vehicle.Id, ct), odometerKm, now);

        var trip = await trips.GetLatestAsync(vehicle.Id, now, ct);

        return new VehicleFacts(
            info?.ApkExpiresOn,
            info?.ApkExpiresOn is { } expiresOn ? ApkReminder.DaysLeft(expiresOn, now) : null,
            info is null ? null : RdwRecalls.Parse(info.RecallsJson).Count(r => r.IsOpen),
            info?.WamInsured,
            next?.Item.Name,
            next?.Due.Status.ApiName(),
            next?.Due.NextDueDate,
            next?.Due.DaysRemaining,
            next?.Due.KmRemaining is { } km ? Math.Round(km) : null,
            trip?.StartedAt,
            trip?.EndedAt,
            trip is null ? null : Math.Round(trip.DistanceKm, 1),
            trip is null ? null : trip.Id is null);
    }

    // Overdue before due soon before fine; among equals, the one with the fewest days, then the
    // fewest kilometres, to go. Items that cannot be judged yet (no last service) come last.
    private static (MaintenanceItem Item, MaintenanceDueResult Due)? MostUrgent(
        IReadOnlyList<MaintenanceItem> items, double? odometerKm, DateTime now) =>
        items
            .Select(item => (Item: item, Due: MaintenanceDueCalculator.Calculate(
                item.IntervalKm, item.IntervalMonths, item.LastServiceDate, item.LastServiceOdometerKm, odometerKm, now)))
            .OrderByDescending(x => x.Due.Status == MaintenanceDueStatus.Unknown ? -1 : (int)x.Due.Status)
            .ThenBy(x => x.Due.DaysRemaining ?? int.MaxValue)
            .ThenBy(x => x.Due.KmRemaining ?? double.MaxValue)
            .Cast<(MaintenanceItem, MaintenanceDueResult)?>()
            .FirstOrDefault();
}
