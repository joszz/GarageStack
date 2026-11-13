using System.Reflection;
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
        e.StartPlace,
        e.EndPlace,
        e.Purpose,
        e.PurposeIsAutomatic,
        e.Notes,
        e.IsHidden,
        e.IsEdited,
        Max = Math.Round(e.MaxSpeedKmh ?? -1, 6),
        Avg = Math.Round(e.AvgMovingSpeedKmh ?? -1, 6),
    };

    // What the statistics read of the finished trips.
    private static object Summed(TripSummaryDto t) => new
    {
        t.StartedAt,
        t.EndedAt,
        Distance = Math.Round(t.DistanceKm, 6),
        t.PointCount,
        t.EndLatitude,
        t.EndLongitude,
        Max = Math.Round(t.MaxSpeedKmh ?? -1, 6),
        Avg = Math.Round(t.AvgMovingSpeedKmh ?? -1, 6),
        t.MovingSpeedSamples,
    };

    // A route as the data export writes it, minus the trip's id.
    private static object Exported(TripRoute r) => new
    {
        Points = Fixes(r.Points),
        Original = r.OriginalPoints is null ? null : Fixes(r.OriginalPoints),
        Edited = r.EditedAt is not null,
    };

    private static string Fixes(IEnumerable<TripPoint> points) =>
        string.Join(";", points.Select(p => $"{p.RecordedAt:O},{p.Latitude:R},{p.Longitude:R},{p.Source}"));

    // What the map draws of the finished trips.
    private static object Drawn(TripDto t) => new { t.StartedAt, t.EndedAt, t.PointCount, Distance = Math.Round(t.DistanceKm, 6) };

    private async Task AssertSameAsync(CancellationToken ct)
    {
        var demo = await _demo.GetLogAsync(_vehicleId, From, To, ct);
        var real = await _real.GetLogAsync(_vehicleId, From, To, ct);
        Assert.Equal(demo.Select(Shown), real.Select(Shown));

        var demoMap = (await _demo.GetTripsAsync(_vehicleId, From, To, ct)).Where(t => t.Id is not null);
        var realMap = await _real.GetTripsAsync(_vehicleId, From, To, ct);
        Assert.Equal(demoMap.Select(Drawn), realMap.Select(Drawn));

        var demoStats = (await _demo.GetSummariesAsync(_vehicleId, From, To, ct)).Where(t => t.Id is not null);
        var realStats = await _real.GetSummariesAsync(_vehicleId, From, To, ct);
        Assert.Equal(demoStats.Select(Summed), realStats.Select(Summed));

        var demoRoutes = await _demo.GetRoutesAsync(_vehicleId, From, To, ct).ToListAsync(ct);
        var realRoutes = await _real.GetRoutesAsync(_vehicleId, From, To, ct).ToListAsync(ct);
        Assert.Equal(demoRoutes.Select(Exported), realRoutes.Select(Exported));
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

    [Fact]
    public async Task ABulkPurpose_AndARelabel_ChangeTheSameTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);
        var log = await _demo.GetLogAsync(_vehicleId, From, To, ct);

        var demoChanged = await _demo.SetPurposeAsync(_vehicleId, [_demoIds[0], _demoIds[1], _demoIds[2]], TripPurpose.Private, ct);
        var realChanged = await _real.SetPurposeAsync(_vehicleId, [_realIds[0], _realIds[1], _realIds[2]], TripPurpose.Private, ct);
        Assert.Equal(demoChanged, realChanged);
        await AssertSameAsync(ct);

        // Home where the last trip starts, work where it ends: only trips nobody labelled follow.
        CommutePlace[] places =
        [
            new() { VehicleId = _vehicleId, Kind = CommutePlaceKind.Home, Latitude = log[^1].StartLatitude, Longitude = log[^1].StartLongitude },
            new() { VehicleId = _vehicleId, Kind = CommutePlaceKind.Work, Latitude = log[^1].EndLatitude, Longitude = log[^1].EndLongitude },
        ];
        _db.CommutePlaces.AddRange(places);
        await _db.SaveChangesAsync(ct);
        Assert.Equal(await _demo.RelabelCommutesAsync(_vehicleId, places, ct), await _real.RelabelCommutesAsync(_vehicleId, places, ct));
        await AssertSameAsync(ct);
    }

    [Fact]
    public async Task AddressesFound_ShowTheSame_AndTheJoinedTripEndsAtTheSecondTripsAddress()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);
        var station = new PlaceAddress("Station 1, Utrecht", "Station", "1", "Utrecht", "3511", "nl");
        var office = new PlaceAddress("Kantoor 2, Zeist", "Kantoor", "2", "Zeist", "3701", "nl");

        await _demo.SavePlacesAsync(_vehicleId, [new(_demoIds[5], station, null), new(_demoIds[6], null, office)], ct);
        await _real.SavePlacesAsync(_vehicleId, [new(_realIds[5], station, null), new(_realIds[6], null, office)], ct);
        await AssertSameAsync(ct);

        await OnBothAsync(5, (repo, id) => repo.MergeWithNextAsync(_vehicleId, id, ct));
        await AssertSameAsync(ct);
    }

    [Fact]
    public async Task TheEditor_OffersTheSameRouteStopsAndLimits()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);

        // A trip with a trip on either side, so both limits are another trip's.
        var demo = (await _demo.GetEditorAsync(_vehicleId, _demoIds[3], ct)).Editor!;
        var real = (await _real.GetEditorAsync(_vehicleId, _realIds[3], ct)).Editor!;

        Assert.Equal(Fixes(demo.Points), Fixes(real.Points));
        Assert.Equal((demo.OriginalPoints, demo.EditedAt), (real.OriginalPoints, real.EditedAt));
        Assert.Equal((demo.NotBefore, demo.NotAfter, demo.MaxPoints), (real.NotBefore, real.NotAfter, real.MaxPoints));
        Assert.Equal(demo.PreviousStop, real.PreviousStop);
        Assert.Equal(demo.NextStop, real.NextStop);
        Assert.Equal(demo.Suggestion, real.Suggestion);
    }

    [Fact]
    public async Task WhatIsRefused_IsRefusedTheSame()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);
        const long missing = 987_654_321;

        Assert.Equal(TripMergeOutcome.NotFound, (await _demo.MergeWithNextAsync(_vehicleId, missing, ct)).Outcome);
        Assert.Equal(TripMergeOutcome.NotFound, (await _real.MergeWithNextAsync(_vehicleId, missing, ct)).Outcome);
        Assert.Equal(TripMergeOutcome.NoNextTrip, (await _demo.MergeWithNextAsync(_vehicleId, _demoIds[^1], ct)).Outcome);
        Assert.Equal(TripMergeOutcome.NoNextTrip, (await _real.MergeWithNextAsync(_vehicleId, _realIds[^1], ct)).Outcome);

        Assert.Equal(TripEditOutcome.NotFound, (await _demo.GetEditorAsync(_vehicleId, missing, ct)).Outcome);
        Assert.Equal(TripEditOutcome.NotFound, (await _real.GetEditorAsync(_vehicleId, missing, ct)).Outcome);
        Assert.Null(await _demo.SetHiddenAsync(_vehicleId, missing, true, ct));
        Assert.Null(await _real.SetHiddenAsync(_vehicleId, missing, true, ct));
        Assert.Null(await _demo.SetPurposeAndNotesAsync(_vehicleId, missing, TripPurpose.Business, null, ct));
        Assert.Null(await _real.SetPurposeAndNotesAsync(_vehicleId, missing, TripPurpose.Business, null, ct));

        // A route of one point is no route.
        var editor = (await _demo.GetEditorAsync(_vehicleId, _demoIds[2], ct)).Editor!;
        var one = editor.Points.Take(1).Select(p => new TripPointInput(new DateTimeOffset(p.RecordedAt), p.Latitude, p.Longitude)).ToList();
        var demoRefusal = await _demo.SetPointsAsync(_vehicleId, _demoIds[2], one, ct);
        var realRefusal = await _real.SetPointsAsync(_vehicleId, _realIds[2], one, ct);
        Assert.Equal((TripEditOutcome.Invalid, demoRefusal.Problem!.Code), (realRefusal.Outcome, realRefusal.Problem!.Code));
        await AssertSameAsync(ct);
    }

    [Fact]
    public async Task ShowingAHiddenTripAgain_ShowsTheSame()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);

        await OnBothAsync(2, (repo, id) => repo.SetHiddenAsync(_vehicleId, id, true, ct));
        await OnBothAsync(2, (repo, id) => repo.SetHiddenAsync(_vehicleId, id, false, ct));

        await AssertSameAsync(ct);
    }

    // The repository methods these tests take on both sides. A method added to ITripRepository has
    // to land here, with a step above that takes it on both, or in Exempt with the reason the demo
    // may differ.
    private static readonly string[] Covered =
    [
        nameof(ITripRepository.GetTripsAsync),
        nameof(ITripRepository.GetSummariesAsync),
        nameof(ITripRepository.GetLogAsync),
        nameof(ITripRepository.GetLogEntriesAsync),
        nameof(ITripRepository.SetPurposeAndNotesAsync),
        nameof(ITripRepository.SetPurposeAsync),
        nameof(ITripRepository.RelabelCommutesAsync),
        nameof(ITripRepository.SetHiddenAsync),
        nameof(ITripRepository.MergeWithNextAsync),
        nameof(ITripRepository.GetRoutesAsync),
        nameof(ITripRepository.GetEditorAsync),
        nameof(ITripRepository.SetPointsAsync),
        nameof(ITripRepository.ResetPointsAsync),
        nameof(ITripRepository.SavePlacesAsync),
    ];

    private static readonly Dictionary<string, string> Exempt = new()
    {
        [nameof(ITripRepository.SaveRecordedAsync)] = "Demo mode runs no Worker, so it records nothing; the tests use it only to seed the real side.",
        [nameof(ITripRepository.GetRecordedUntilAsync)] = "Demo mode has no recording line.",
        [nameof(ITripRepository.FillSpeedFiguresAsync)] = "Demo trips carry their speed figures from the start, so there is nothing to fill.",
        [nameof(ITripRepository.GetLatestAsync)] = "The demo's timeline ends with the trip it is driving, whatever the clock says.",
    };

    [Fact]
    public void EveryRepositoryMethod_IsTakenOnBothSides_OrExemptWithAReason()
    {
        var methods = typeof(ITripRepository).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).ToHashSet();
        var accounted = Covered.Concat(Exempt.Keys).ToList();

        Assert.Empty(methods.Except(accounted));
        Assert.Empty(accounted.Except(methods));
        Assert.Equal(accounted.Count, accounted.Distinct().Count());
    }
}
