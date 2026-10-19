using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.Extensions.Logging;

namespace GarageStack.Data.Services;

/// <summary>
/// One RDW lookup for one vehicle, saved: the registration first, then its recall statuses, and
/// the details only of recalls not seen before. The API runs it as soon as a plate is entered and
/// the Worker once a lookup is older than RDW_REFRESH_HOURS, so both write the same row the same way.
/// </summary>
public sealed class RdwRefresher(
    IRdwClient client,
    IRdwRepository repository,
    RdwOptions options,
    TimeProvider time,
    ILogger<RdwRefresher> logger)
{
    public bool Enabled => options.Enabled;

    /// <summary>
    /// Whether <paramref name="vehicle"/> is due a lookup: it has a plate, and the last good answer
    /// for that plate is older than the refresh interval (or there is none).
    /// </summary>
    public bool IsDue(Vehicle vehicle, RdwVehicleInfo? info) =>
        options.Enabled
        && vehicle.LicensePlate is not null
        && (info is not { FetchedAt: { } fetchedAt }
            || info.LicensePlate != vehicle.LicensePlate
            || time.GetUtcNow().UtcDateTime - fetchedAt >= options.RefreshInterval);

    /// <summary>
    /// Asks the RDW about the vehicle's plate now and saves the answer, or the reason there is
    /// none. Null, having asked nothing, when lookups are off or the vehicle has no plate.
    /// </summary>
    public async Task<RdwVehicleInfo?> RefreshAsync(Vehicle vehicle, CancellationToken ct = default)
    {
        if (!options.Enabled || vehicle.LicensePlate is not { } plate) return null;

        var existing = await repository.GetAsync(vehicle.Id, ct);
        IReadOnlyList<RdwRecall> known = existing?.LicensePlate == plate ? RdwRecalls.Parse(existing.RecallsJson) : [];

        RdwVehicleInfo info;
        try
        {
            info = await LookUpAsync(vehicle.Id, plate, known, ct);
        }
        catch (RdwUnavailableException ex)
        {
            // The plate stays out of the log: it is the one thing about the car that leaves the network.
            logger.LogWarning("RDW lookup for vehicle {VehicleId} failed: {Reason}", vehicle.Id, ex.Message);
            await repository.SetErrorAsync(vehicle.Id, plate, ex.Message, ct);
            return await repository.GetAsync(vehicle.Id, ct);
        }

        await repository.SaveAsync(info, ct);
        logger.LogInformation("RDW lookup for vehicle {VehicleId}: plate known {Found}, {OpenRecalls} open recall(s)",
            vehicle.Id, info.Found, RdwRecalls.Parse(info.RecallsJson).Count(r => r.IsOpen));
        return await repository.GetAsync(vehicle.Id, ct);
    }

    private async Task<RdwVehicleInfo> LookUpAsync(int vehicleId, string plate, IReadOnlyList<RdwRecall> known, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var registration = await client.GetRegistrationAsync(plate, ct);
        if (registration is null)
            return new RdwVehicleInfo { VehicleId = vehicleId, LicensePlate = plate, Found = false, FetchedAt = now };

        var statuses = await client.GetRecallStatusesAsync(plate, ct);
        var unseen = RdwRecalls.Unseen(statuses, known);
        var details = unseen.Count > 0 ? await client.GetRecallDetailsAsync(unseen, ct) : [];

        return new RdwVehicleInfo
        {
            VehicleId = vehicleId,
            LicensePlate = plate,
            Found = true,
            Make = registration.Make,
            Model = registration.Model,
            ApkExpiresOn = registration.ApkExpiresOn,
            WamInsured = registration.WamInsured,
            OpenRecallIndicator = registration.OpenRecallIndicator,
            RecallsJson = RdwRecalls.Serialize(RdwRecalls.Merge(known, statuses, details, now)),
            FetchedAt = now,
        };
    }
}
