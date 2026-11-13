using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Demo;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

/// <summary>
/// The demo's trip repository against the real one. Both hold the demo's trips (the real one
/// seeded with exactly what the demo serves), the same steps are taken on each, and their trip logs
/// and maps must come out the same. A trip feature built only on one side fails here instead of
/// drifting unnoticed.
/// </summary>
public sealed class TripRepositoryContractTests : IAsyncDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private readonly DemoTripRepository _demo = new();
    private TripRepository _real = null!;
    private int _vehicleId;

    // Each repository's trip ids in log order, so a step can name "the third trip" on both.
    private IReadOnlyList<long> _demoIds = [];
    private IReadOnlyList<long> _realIds = [];

    private static readonly DateTime From = DateTime.UtcNow.AddYears(-1);
    private static readonly DateTime To = DateTime.UtcNow.AddDays(1);

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private async Task SeedAsync(CancellationToken ct)
    {
        var vehicle = new Vehicle { Vin = "FAKEVN00000000001" };
        _db.Vehicles.Add(vehicle);
        await _db.SaveChangesAsync(ct);
        _vehicleId = vehicle.Id;
        _real = new TripRepository(_db, new TelemetryRepository(_db));

        var log = await _demo.GetLogAsync(_vehicleId, From, To, ct);
        var trips = (await _demo.GetTripsAsync(_vehicleId, From, To, ct)).Where(t => t.Id is not null).ToDictionary(t => t.Id!.Value);
        await _real.SaveRecordedAsync(_vehicleId,
            [.. log.Select(e => new RecordedTrip(trips[e.Id] with { Id = null }, e.OdometerStartKm, e.OdometerEndKm))],
            log[^1].EndedAt.AddMinutes(1), ct);

        _demoIds = [.. log.Select(e => e.Id)];
        _realIds = [.. (await _real.GetLogAsync(_vehicleId, From, To, ct)).Select(e => e.Id)];
        for (var i = 0; i < log.Count; i++)
        {
            if (log[i].Purpose is not null || log[i].Notes is not null)
                await _real.SetPurposeAndNotesAsync(_vehicleId, _realIds[i], log[i].Purpose, log[i].Notes, ct);
        }
    }

    // One step on both repositories, with the trip it names by its position in the log.
    private async Task OnBothAsync(int index, Func<ITripRepository, long, Task> step)
    {
        await step(_demo, _demoIds[index]);
        await step(_real, _realIds[index]);
    }

    // What the trip log shows of a trip, minus its id; rounded where the two count a sum in another order.
    private static object Shown(TripLogEntry e) => new
    {
        e.StartedAt,
        e.EndedAt,
        Distance = Math.Round(e.DistanceKm, 6),
        e.StartLatitude,
        e.StartLongitude,
        e.EndLatitude,
        e.EndLongitude,
        e.OdometerStartKm,
        e.OdometerEndKm,
        e.Purpose,
        e.PurposeIsAutomatic,
        e.Notes,
        e.IsHidden,
        e.IsEdited,
        Max = Math.Round(e.MaxSpeedKmh ?? -1, 6),
        Avg = Math.Round(e.AvgMovingSpeedKmh ?? -1, 6),
    };

    // What the map draws of the finished trips.
    private static object Drawn(TripDto t) => new { t.StartedAt, t.EndedAt, t.PointCount };

    private async Task AssertSameAsync(CancellationToken ct)
    {
        var demo = await _demo.GetLogAsync(_vehicleId, From, To, ct);
        var real = await _real.GetLogAsync(_vehicleId, From, To, ct);
        Assert.Equal(demo.Select(Shown), real.Select(Shown));

        var demoMap = (await _demo.GetTripsAsync(_vehicleId, From, To, ct)).Where(t => t.Id is not null);
        var realMap = await _real.GetTripsAsync(_vehicleId, From, To, ct);
        Assert.Equal(demoMap.Select(Drawn), realMap.Select(Drawn));
    }

    [Fact]
    public async Task BothStartFromTheSameTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);

        await AssertSameAsync(ct);
    }

    [Fact]
    public async Task AJoin_GivesTheSameTrip_DistanceNotesAndPurposeIncluded()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);
        await OnBothAsync(1, (repo, id) => repo.SetPurposeAndNotesAsync(_vehicleId, id, TripPurpose.Business, "Fuel stop", ct));
        await OnBothAsync(2, (repo, id) => repo.SetPurposeAndNotesAsync(_vehicleId, id, TripPurpose.Business, "Client", ct));

        await OnBothAsync(1, (repo, id) => repo.MergeWithNextAsync(_vehicleId, id, ct));

        await AssertSameAsync(ct);
    }

    [Fact]
    public async Task JoiningTwice_MeasuresTheSecondStopFromTheJoinedRoute()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);

        await OnBothAsync(0, (repo, id) => repo.MergeWithNextAsync(_vehicleId, id, ct));
        await OnBothAsync(0, (repo, id) => repo.MergeWithNextAsync(_vehicleId, id, ct));

        await AssertSameAsync(ct);
    }

    [Fact]
    public async Task AJoinOfTripsNobodyLabelled_IsLabelledFromHomeAndWork()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);
        var log = await _demo.GetLogAsync(_vehicleId, From, To, ct);
        var first = Enumerable.Range(0, log.Count - 1).First(i => log[i].Purpose is null && log[i + 1].Purpose is null);
        CommutePlace[] places =
        [
            new() { VehicleId = _vehicleId, Kind = CommutePlaceKind.Home, Latitude = log[first].StartLatitude, Longitude = log[first].StartLongitude },
            new() { VehicleId = _vehicleId, Kind = CommutePlaceKind.Work, Latitude = log[first + 1].EndLatitude, Longitude = log[first + 1].EndLongitude },
        ];
        _db.CommutePlaces.AddRange(places);
        await _db.SaveChangesAsync(ct);
        await _demo.RelabelCommutesAsync(_vehicleId, places, ct);
        await _real.RelabelCommutesAsync(_vehicleId, places, ct);

        await OnBothAsync(first, (repo, id) => repo.MergeWithNextAsync(_vehicleId, id, ct));

        Assert.Equal(TripPurpose.Commute, (await _demo.GetLogEntriesAsync(_vehicleId, [_demoIds[first]], ct))[0].Purpose);
        await AssertSameAsync(ct);
    }

    [Fact]
    public async Task HidingAndJoiningAHiddenTrip_ShowTheSame()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);

        await OnBothAsync(3, (repo, id) => repo.SetHiddenAsync(_vehicleId, id, true, ct));
        await AssertSameAsync(ct);

        await OnBothAsync(3, (repo, id) => repo.MergeWithNextAsync(_vehicleId, id, ct));
        await AssertSameAsync(ct);
    }

    [Fact]
    public async Task AnEditedRoute_ThenAJoin_ThenAReset_ShowTheSame()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);

        // Leaves out the second fix: the same edit, made the same way on both.
        await OnBothAsync(4, async (repo, id) =>
        {
            var editor = (await repo.GetEditorAsync(_vehicleId, id, ct)).Editor!;
            var points = editor.Points.Where((_, i) => i != 1)
                .Select(p => new TripPointInput(new DateTimeOffset(p.RecordedAt), p.Latitude, p.Longitude)).ToList();
            Assert.Equal(TripEditOutcome.Ok, (await repo.SetPointsAsync(_vehicleId, id, points, ct)).Outcome);
        });
        await AssertSameAsync(ct);

        await OnBothAsync(4, (repo, id) => repo.MergeWithNextAsync(_vehicleId, id, ct));
        await AssertSameAsync(ct);

        await OnBothAsync(4, (repo, id) => repo.ResetPointsAsync(_vehicleId, id, ct));
        await AssertSameAsync(ct);
    }
}
