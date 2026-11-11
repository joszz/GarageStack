using System.Runtime.CompilerServices;
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

    // Trips a visitor hid, and the ones joined into the trip before them: both stay off the map.
    private readonly HashSet<long> _gone = [];

    // The routes a visitor edited, and when, by trip id.
    private readonly Dictionary<long, (IReadOnlyList<TripPoint> Points, DateTime At)> _routes = [];

    public Task<IReadOnlyList<TripDto>> GetTripsAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<TripDto>>(
                [.. DemoTrips.All
                    .Where(t => t.StartedAt >= from && t.StartedAt < to && (t.Id is not { } id || !_gone.Contains(id)))
                    .Select((t, i) => Current(t) with { Index = i })]);
        }
    }

    public async Task<IReadOnlyList<TripSummaryDto>> GetSummariesAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default) =>
        [.. (await GetTripsAsync(vehicleId, from, to, ct)).Select(t => t.ToSummary())];

    // Every demo trip carries its fixes, so there is nothing to fill in.
    public Task<int> FillSpeedFiguresAsync(int batchSize, CancellationToken ct = default) => Task.FromResult(0);

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

    public Task<TripLogEntry?> SetHiddenAsync(int vehicleId, long id, bool hidden, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_log.TryGetValue(id, out var entry)) return Task.FromResult<TripLogEntry?>(null);
            if (hidden) _gone.Add(id);
            else _gone.Remove(id);
            return Task.FromResult<TripLogEntry?>(_log[id] = entry with { IsHidden = hidden });
        }
    }

    // The demo's trips are drawn from a fixed timeline, so a join shows in the trip log; the map
    // keeps drawing the first trip's own line.
    public Task<TripMergeResult> MergeWithNextAsync(int vehicleId, long id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_log.TryGetValue(id, out var first)) return Task.FromResult(new TripMergeResult(TripMergeOutcome.NotFound));
            var second = _log.Values.Where(e => e.StartedAt > first.StartedAt).MinBy(e => e.StartedAt);
            if (second is null) return Task.FromResult(new TripMergeResult(TripMergeOutcome.NoNextTrip));

            var joined = TripEdit.Summarize([.. CurrentPoints(first.Id), .. CurrentPoints(second.Id)], id);
            var merged = first with
            {
                EndedAt = second.EndedAt,
                DistanceKm = first.DistanceKm + second.DistanceKm,
                EndLatitude = second.EndLatitude,
                EndLongitude = second.EndLongitude,
                OdometerEndKm = second.OdometerEndKm,
                EndPlace = second.EndPlace,
                IsHidden = first.IsHidden && second.IsHidden,
                MaxSpeedKmh = joined.MaxSpeedKmh,
                AvgMovingSpeedKmh = joined.AvgMovingSpeedKmh,
            };
            _log[id] = merged;
            _log.Remove(second.Id);
            _gone.Add(second.Id);
            return Task.FromResult(new TripMergeResult(TripMergeOutcome.Merged, merged));
        }
    }

    public async IAsyncEnumerable<TripRoute> GetRoutesAsync(int vehicleId, DateTime from, DateTime to, [EnumeratorCancellation] CancellationToken ct = default)
    {
        List<TripRoute> routes;
        lock (_lock)
        {
            routes = [.. _log.Values
                .Where(e => e.StartedAt >= from && e.StartedAt < to)
                .OrderBy(e => e.StartedAt)
                .Select(e => Recorded(e.Id))
                .OfType<TripDto>()
                .Select(recorded => Editor(recorded))
                .Select(editor => new TripRoute(editor.Id, editor.Points, editor.OriginalPoints, editor.EditedAt))];
        }
        foreach (var route in routes)
        {
            ct.ThrowIfCancellationRequested();
            yield return route;
        }
        await Task.CompletedTask;
    }

    public Task<TripEditResult> GetEditorAsync(int vehicleId, long id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (Recorded(id) is not { } recorded) return Task.FromResult(new TripEditResult(TripEditOutcome.NotFound));
            return Task.FromResult(new TripEditResult(TripEditOutcome.Ok, Editor(recorded)));
        }
    }

    public Task<TripEditResult> SetPointsAsync(int vehicleId, long id, IReadOnlyList<TripPointInput>? points, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (Recorded(id) is not { } recorded) return Task.FromResult(new TripEditResult(TripEditOutcome.NotFound));

            var (previous, next) = Neighbours(recorded);
            if (TripEdit.TryBuild(points, recorded.Points, previous?.EndedAt, NotAfter(next), out var built) is { } problem)
                return Task.FromResult(new TripEditResult(TripEditOutcome.Invalid, Problem: problem));

            _routes[id] = (built, DateTime.UtcNow);
            ApplyRoute(id, built, edited: true);
            return Task.FromResult(new TripEditResult(TripEditOutcome.Ok, Editor(recorded)));
        }
    }

    public Task<TripEditResult> ResetPointsAsync(int vehicleId, long id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (Recorded(id) is not { } recorded) return Task.FromResult(new TripEditResult(TripEditOutcome.NotFound));

            if (_routes.Remove(id)) ApplyRoute(id, recorded.Points, edited: false);
            return Task.FromResult(new TripEditResult(TripEditOutcome.Ok, Editor(recorded)));
        }
    }

    // A trip's fixes as a visitor left them, or none for a trip the timeline does not hold.
    private IReadOnlyList<TripPoint> CurrentPoints(long id) =>
        DemoTrips.All.FirstOrDefault(t => t.Id == id) is { } recorded ? Current(recorded).Points : [];

    // A finished demo trip still in the log, as the demo timeline recorded it.
    private TripDto? Recorded(long id) =>
        _log.ContainsKey(id) ? DemoTrips.All.FirstOrDefault(t => t.Id == id) : null;

    // The trip as a visitor left it: their route when they edited one.
    private TripDto Current(TripDto recorded) =>
        recorded.Id is { } id && _routes.TryGetValue(id, out var route)
            ? TripEdit.Summarize(route.Points, id) with { Index = recorded.Index }
            : recorded;

    // The finished trips either side, by where they start now. The trip being driven stands in for
    // the recording line: no edit may run into it.
    private (TripDto? Previous, TripDto? Next) Neighbours(TripDto recorded)
    {
        var finished = DemoTrips.All
            .Where(t => t.Id is { } other && other != recorded.Id && _log.ContainsKey(other))
            .Select(Current)
            .ToList();
        var start = Current(recorded).StartedAt;
        return (finished.Where(t => t.StartedAt < start).MaxBy(t => t.StartedAt),
                finished.Where(t => t.StartedAt > start).MinBy(t => t.StartedAt));
    }

    private static DateTime? NotAfter(TripDto? next) =>
        next?.StartedAt ?? DemoTrips.All.LastOrDefault(t => t.Id is null)?.StartedAt;

    // The demo has no parked fixes, so the car stood where the trips either side end and start.
    private TripEditor Editor(TripDto recorded)
    {
        var current = Current(recorded);
        var edited = _routes.TryGetValue(recorded.Id!.Value, out var route) ? route : default;
        var (previous, next) = Neighbours(recorded);
        var first = current.Points[0];
        var last = current.Points[^1];

        var previousStop = previous is null
            ? null
            : TripEdit.Departing(previous.Points[^1], TripAnchor.Departure(previous.Points[^1], first, null));
        var nextStop = next is null ? null : TripEdit.Arriving(last, next.Points[0], isParkedFix: false);

        return TripEdit.Editor(
            recorded.Id!.Value, current.Points, edited.Points is null ? null : recorded.Points, edited.Points is null ? null : edited.At,
            previousStop, nextStop, previous?.EndedAt, NotAfter(next));
    }

    // The trip log follows the route, as the saved trip's row does. The demo labels commutes only
    // when home or work move, so the purpose stays.
    private void ApplyRoute(long id, IReadOnlyList<TripPoint> points, bool edited)
    {
        var entry = _log[id];
        var trip = TripEdit.Summarize(points, id);
        _log[id] = entry with
        {
            StartedAt = trip.StartedAt,
            EndedAt = trip.EndedAt,
            DistanceKm = trip.DistanceKm,
            StartLatitude = points[0].Latitude,
            StartLongitude = points[0].Longitude,
            EndLatitude = points[^1].Latitude,
            EndLongitude = points[^1].Longitude,
            StartPlace = TripEdit.AddressMoved(entry.StartLatitude, entry.StartLongitude, points[0]) ? null : entry.StartPlace,
            EndPlace = TripEdit.AddressMoved(entry.EndLatitude, entry.EndLongitude, points[^1]) ? null : entry.EndPlace,
            IsEdited = edited,
            MaxSpeedKmh = trip.MaxSpeedKmh,
            AvgMovingSpeedKmh = trip.AvgMovingSpeedKmh,
        };
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
