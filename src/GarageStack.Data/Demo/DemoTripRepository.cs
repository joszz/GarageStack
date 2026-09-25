using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

/// <summary>
/// Serves the demo's fixed trips. Demo mode runs no Worker, so nothing is ever recorded: saving is
/// a no-op and the recording line is never set. What a visitor records in the trip log is kept in
/// memory for as long as the demo runs.
/// </summary>
public sealed class DemoTripRepository : ITripRepository
{
    private readonly Lock _lock = new();
    private readonly Dictionary<long, TripLogEntry> _log = DemoTrips.Log.ToDictionary(e => e.Id);

    public Task<IReadOnlyList<TripDto>> GetTripsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TripDto>>(
            [.. DemoTrips.All
                .Where(t => t.StartedAt >= from && t.StartedAt < to)
                .Select((t, i) => t with { Index = i })]);

    public Task<DateTime?> GetRecordedUntilAsync(int vehicleId, CancellationToken ct = default) =>
        Task.FromResult<DateTime?>(null);

    public Task SaveRecordedAsync(int vehicleId, IReadOnlyList<RecordedTrip> trips, DateTime recordedUntil, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<TripLogEntry>> GetLogAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<TripLogEntry>>(
                [.. _log.Values.Where(e => e.StartedAt >= from && e.StartedAt < to).OrderBy(e => e.StartedAt)]);
        }
    }

    public Task<IReadOnlyList<TripLogEntry>> GetLogEntriesAsync(int vehicleId, IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<TripLogEntry>>(
                [.. ids.Distinct().Select(id => _log.GetValueOrDefault(id)).OfType<TripLogEntry>()]);
        }
    }

    public Task<TripLogEntry?> SetPurposeAndNotesAsync(int vehicleId, long id, TripPurpose? purpose, string? notes, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_log.TryGetValue(id, out var entry)) return Task.FromResult<TripLogEntry?>(null);
            var updated = entry with { Purpose = purpose, Notes = notes };
            _log[id] = updated;
            return Task.FromResult<TripLogEntry?>(updated);
        }
    }

    public Task<int> SetPurposeAsync(int vehicleId, IReadOnlyCollection<long> ids, TripPurpose? purpose, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var changed = 0;
            foreach (var id in ids.Distinct())
            {
                if (!_log.TryGetValue(id, out var entry)) continue;
                _log[id] = entry with { Purpose = purpose };
                changed++;
            }

            return Task.FromResult(changed);
        }
    }

    public Task SavePlacesAsync(int vehicleId, IReadOnlyList<TripPlaces> places, CancellationToken ct = default)
    {
        lock (_lock)
        {
            foreach (var place in places)
            {
                if (!_log.TryGetValue(place.Id, out var entry)) continue;
                _log[place.Id] = entry with
                {
                    StartPlace = place.StartPlace ?? entry.StartPlace,
                    EndPlace = place.EndPlace ?? entry.EndPlace,
                };
            }
        }

        return Task.CompletedTask;
    }
}
