using GarageStack.Core.Helpers;
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

    // The trips a visitor chose a purpose for, as Trip.PurposeIsManual records it for real trips.
    private readonly HashSet<long> _manual = [.. DemoTrips.Log.Where(e => e.Purpose is not null).Select(e => e.Id)];

    public Task<IReadOnlyList<TripDto>> GetTripsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TripDto>>(
            [.. DemoTrips.All
                .Where(t => t.StartedAt >= from && t.StartedAt < to)
                .Select((t, i) => t with { Index = i })]);

    // The demo's fixed timeline ends with the trip it is driving, whatever the clock says.
    public Task<TripDto?> GetLatestAsync(int vehicleId, DateTime now, CancellationToken ct = default) =>
        Task.FromResult<TripDto?>(DemoTrips.All.Count > 0 ? DemoTrips.All[^1] with { Index = 0 } : null);

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
            var updated = entry with { Notes = notes };
            if (entry.Purpose != purpose)
            {
                _manual.Add(id);
                updated = updated with { Purpose = purpose, PurposeIsAutomatic = false };
            }
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
                _log[id] = entry with { Purpose = purpose, PurposeIsAutomatic = false };
                _manual.Add(id);
                changed++;
            }

            return Task.FromResult(changed);
        }
    }

    public Task<CommuteRelabel> RelabelCommutesAsync(int vehicleId, IReadOnlyList<CommutePlace> places, CancellationToken ct = default)
    {
        lock (_lock)
        {
            int marked = 0, cleared = 0;
            foreach (var entry in _log.Values.Where(e => !_manual.Contains(e.Id)).ToList())
            {
                var purpose = CommuteRule.AutomaticPurpose(
                    entry.StartLatitude, entry.StartLongitude, entry.EndLatitude, entry.EndLongitude, places);
                if (purpose == entry.Purpose) continue;

                _log[entry.Id] = entry with { Purpose = purpose, PurposeIsAutomatic = purpose is not null };
                if (purpose is null) cleared++;
                else marked++;
            }

            return Task.FromResult(new CommuteRelabel(marked, cleared));
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
