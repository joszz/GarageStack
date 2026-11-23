using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

/// <summary>
/// Serves each demo car's fixed trips, and keeps what a visitor does to them apart per car: a trip
/// hidden on one car stays on the other. The work is <see cref="DemoCarTrips"/>'s, one for each car.
/// </summary>
public sealed class DemoTripRepository : ITripRepository
{
    private readonly Dictionary<int, DemoCarTrips> _cars = [];

    // A car's trips and what a visitor did to them, made when first asked for.
    private DemoCarTrips For(int vehicleId)
    {
        var car = DemoCars.For(vehicleId);
        lock (_cars)
        {
            if (!_cars.TryGetValue(car.Vehicle.Id, out var trips))
                _cars[car.Vehicle.Id] = trips = new DemoCarTrips(car.Trips);
            return trips;
        }
    }

    public Task<IReadOnlyList<TripDto>> GetTripsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        For(vehicleId).GetTripsAsync(vehicleId, from, to, ct);

    public Task<IReadOnlyList<TripSummaryDto>> GetSummariesAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        For(vehicleId).GetSummariesAsync(vehicleId, from, to, ct);

    // Every demo trip carries its fixes and what it used, so there is nothing to fill in.
    public Task<int> FillSpeedFiguresAsync(int batchSize, CancellationToken ct = default) => Task.FromResult(0);

    public Task<int> FillCounterUseAsync(int batchSize, CancellationToken ct = default) => Task.FromResult(0);

    public Task<TripDto?> GetLatestAsync(int vehicleId, DateTime now, CancellationToken ct = default) =>
        For(vehicleId).GetLatestAsync(vehicleId, now, ct);

    public Task<DateTime?> GetRecordedUntilAsync(int vehicleId, CancellationToken ct = default) =>
        For(vehicleId).GetRecordedUntilAsync(vehicleId, ct);

    public Task SaveRecordedAsync(int vehicleId, IReadOnlyList<RecordedTrip> trips, DateTime recordedUntil, CancellationToken ct = default) =>
        For(vehicleId).SaveRecordedAsync(vehicleId, trips, recordedUntil, ct);

    public Task<IReadOnlyList<TripLogEntry>> GetLogAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        For(vehicleId).GetLogAsync(vehicleId, from, to, ct);

    public Task<IReadOnlyList<TripLogEntry>> GetLogEntriesAsync(int vehicleId, IReadOnlyCollection<long> ids, CancellationToken ct = default) =>
        For(vehicleId).GetLogEntriesAsync(vehicleId, ids, ct);

    public Task<TripLogEntry?> SetPurposeAndNotesAsync(int vehicleId, long id, TripPurpose? purpose, string? notes, CancellationToken ct = default) =>
        For(vehicleId).SetPurposeAndNotesAsync(vehicleId, id, purpose, notes, ct);

    public Task<int> SetPurposeAsync(int vehicleId, IReadOnlyCollection<long> ids, TripPurpose? purpose, CancellationToken ct = default) =>
        For(vehicleId).SetPurposeAsync(vehicleId, ids, purpose, ct);

    public Task<CommuteRelabel> RelabelCommutesAsync(int vehicleId, IReadOnlyList<CommutePlace> places, CancellationToken ct = default) =>
        For(vehicleId).RelabelCommutesAsync(vehicleId, places, ct);

    public Task<TripLogEntry?> SetHiddenAsync(int vehicleId, long id, bool hidden, CancellationToken ct = default) =>
        For(vehicleId).SetHiddenAsync(vehicleId, id, hidden, ct);

    public Task<TripMergeResult> MergeWithNextAsync(int vehicleId, long id, CancellationToken ct = default) =>
        For(vehicleId).MergeWithNextAsync(vehicleId, id, ct);

    public IAsyncEnumerable<TripRoute> GetRoutesAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        For(vehicleId).GetRoutesAsync(vehicleId, from, to, ct);

    public Task<TripEditResult> GetEditorAsync(int vehicleId, long id, CancellationToken ct = default) =>
        For(vehicleId).GetEditorAsync(vehicleId, id, ct);

    public Task<TripEditResult> SetPointsAsync(int vehicleId, long id, IReadOnlyList<TripPointInput>? points, CancellationToken ct = default) =>
        For(vehicleId).SetPointsAsync(vehicleId, id, points, ct);

    public Task<TripEditResult> ResetPointsAsync(int vehicleId, long id, CancellationToken ct = default) =>
        For(vehicleId).ResetPointsAsync(vehicleId, id, ct);

    public Task SavePlacesAsync(int vehicleId, IReadOnlyList<TripPlaces> places, CancellationToken ct = default) =>
        For(vehicleId).SavePlacesAsync(vehicleId, places, ct);
}
