using System.Collections.Concurrent;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.Extensions.Localization;

namespace GarageStack.Worker.Mqtt;

/// <summary>
/// Tells when the car leaves or reaches a home or work place that asks for it
/// (<see cref="CommutePlace.Notify"/>). <see cref="TelemetryHandler"/> feeds it the engine and the
/// car's positions; this class keeps what one drive has shown so far.
/// <para>
/// Leaving is told on the first position outside the place's radius after the engine started
/// there, not when it started: an engine run to warm the car, or a car moved down the drive, has
/// not left. Reaching is told on the position of the poll the car parked in, which the gateway
/// sends just after the engine stopping (see <see cref="TelemetryHandler"/>), and not for a drive
/// that never left the place it started in.
/// </para>
/// <para>
/// The state is this process's own: a Worker restarted mid-drive tells neither end of that drive,
/// which beats telling one twice.
/// </para>
/// </summary>
public sealed class PlaceAlerts(
    ILogger logger,
    IPushSender pushSender,
    IStringLocalizer<NotificationStrings> strings)
{
    // The place the car started in and has not left yet, per vehicle.
    private readonly ConcurrentDictionary<int, CommutePlace> _leaving = new();

    // A parking whose own position is still to come, and the place the drive never left, if any.
    private readonly ConcurrentDictionary<int, Arrival> _arriving = new();

    private sealed record Arrival(DateTime ParkedAt, int? NeverLeftPlaceId);

    /// <summary>The engine started: a new drive, from where the car was parked.</summary>
    public async Task EngineStartedAsync(
        int vehicleId, Vehicle? vehicle, ICommutePlaceRepository places, CancellationToken ct)
    {
        _arriving.TryRemove(vehicleId, out _);
        _leaving.TryRemove(vehicleId, out _);
        if (vehicle is not { ParkedLatitude: { } latitude, ParkedLongitude: { } longitude }) return;

        if (PlaceAt(await places.ListAsync(vehicleId, ct), latitude, longitude) is { } place)
            _leaving[vehicleId] = place;
    }

    /// <summary>Whether the car started in a place and has not left it yet.</summary>
    public bool IsLeaving(int vehicleId) => _leaving.ContainsKey(vehicleId);

    /// <summary>Where the car is now, while a drive may be leaving a place.</summary>
    public async Task PositionAsync(int vehicleId, double latitude, double longitude, CancellationToken ct)
    {
        if (!_leaving.TryGetValue(vehicleId, out var place) || CommuteRule.IsWithin(place, latitude, longitude)) return;
        // Two messages of one poll may both see the car outside; only one tells it.
        if (!_leaving.TryRemove(vehicleId, out _)) return;

        logger.LogInformation("Vehicle {VehicleId} left commute place {PlaceId}", vehicleId, place.Id);
        await pushSender.SendToAllAsync(
            Title(place, "PlaceLeftHomeTitle", "PlaceLeftWorkTitle", "PlaceLeftTitle"),
            strings["PlaceLeftBody"], ct, NotificationCategories.PlaceDeparture, vehicleId);
    }

    /// <summary>The engine stopped: the drive is over once its parking position is known.</summary>
    public void Parked(int vehicleId, DateTime parkedAt)
    {
        var neverLeft = _leaving.TryRemove(vehicleId, out var place) ? place.Id : (int?)null;
        _arriving[vehicleId] = new Arrival(parkedAt, neverLeft);
    }

    /// <summary>Whether a parking still waits for its position, and when the car parked.</summary>
    public bool IsAwaitingArrival(int vehicleId, out DateTime parkedAt)
    {
        var awaiting = _arriving.TryGetValue(vehicleId, out var arrival);
        parkedAt = arrival?.ParkedAt ?? default;
        return awaiting;
    }

    /// <summary>Where the car parked: tells the arrival, if it is at a place that asks for it.</summary>
    public async Task ParkedAtAsync(
        int vehicleId, double? latitude, double? longitude, ICommutePlaceRepository places, CancellationToken ct)
    {
        if (!_arriving.TryRemove(vehicleId, out var arrival)) return;
        if (latitude is not { } lat || longitude is not { } lon) return;
        if (PlaceAt(await places.ListAsync(vehicleId, ct), lat, lon) is not { } place
            || place.Id == arrival.NeverLeftPlaceId)
            return;

        logger.LogInformation("Vehicle {VehicleId} arrived at commute place {PlaceId}", vehicleId, place.Id);
        await pushSender.SendToAllAsync(
            Title(place, "PlaceArrivedHomeTitle", "PlaceArrivedWorkTitle", "PlaceArrivedTitle"),
            strings["PlaceArrivedBody"], ct, NotificationCategories.PlaceArrival, vehicleId);
    }

    // Home comes first in the list, so where a work place overlaps home, home is where the car is.
    private static CommutePlace? PlaceAt(IReadOnlyList<CommutePlace> places, double latitude, double longitude) =>
        places.FirstOrDefault(p => p.Notify && CommuteRule.IsWithin(p, latitude, longitude));

    // A place the user named is told by its name; otherwise by its kind, in the language's own words.
    private string Title(CommutePlace place, string homeKey, string workKey, string namedKey) =>
        place.Label is { } label
            ? strings[namedKey, label]
            : strings[place.Kind == CommutePlaceKind.Home ? homeKey : workKey];
}
