using System.Text.Json;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Demo;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

// Editing a saved trip's route through the repositories: what the editor is told, what a save
// changes and keeps, and how a reset and a join treat the points as recorded.
public class TripEditorRepositoryTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private sealed class Setup : IAsyncDisposable
    {
        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        public Vehicle Vehicle { get; } = new() { Vin = "FAKEVN00000000001" };
        public TripRepository Repo { get; }

        // Home, a fuel stop and work: two saved trips, the second ten minutes after the first.
        public long ToFuel { get; private set; }
        public long ToWork { get; private set; }

        public Setup() => Repo = new TripRepository(Db, new TelemetryRepository(Db));

        public async Task<Setup> SaveTwoAsync(CancellationToken ct)
        {
            Db.Vehicles.Add(Vehicle);
            await Db.SaveChangesAsync(ct);
            await Repo.SaveRecordedAsync(Vehicle.Id,
                [Leg(T0, 52.00, 52.10, 1000, 1011), Leg(T0.AddMinutes(20), 52.10, 52.20, 1011, 1022)], T0.AddHours(1), ct);
            var ids = await Db.Trips.OrderBy(t => t.StartedAt).Select(t => t.Id).ToListAsync(ct);
            (ToFuel, ToWork) = (ids[0], ids[1]);
            return this;
        }

        public async Task HomeAndWorkAsync(CancellationToken ct)
        {
            Db.CommutePlaces.AddRange(
                new CommutePlace { VehicleId = Vehicle.Id, Kind = CommutePlaceKind.Home, Latitude = 52.00, Longitude = 5.0 },
                new CommutePlace { VehicleId = Vehicle.Id, Kind = CommutePlaceKind.Work, Latitude = 52.20, Longitude = 5.0 });
            await Db.SaveChangesAsync(ct);
        }

        public async Task AddParkedFixAsync(DateTime at, double latitude, CancellationToken ct)
        {
            Db.TelemetrySnapshots.Add(new TelemetrySnapshot
            {
                VehicleId = Vehicle.Id,
                RecordedAt = at,
                Latitude = latitude,
                Longitude = 5.0,
                Speed = 0,
            });
            await Db.SaveChangesAsync(ct);
        }

        public async Task<Trip> RowAsync(long id, CancellationToken ct)
        {
            Db.ChangeTracker.Clear();
            return await Db.Trips.AsNoTracking().SingleAsync(t => t.Id == id, ct);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    // A drive north from one latitude to another, a fix every five minutes.
    private static RecordedTrip Leg(DateTime start, double from, double to, double odometerStart, double odometerEnd)
    {
        TripPoint[] fixes =
        [
            new(start, from, 5.0, 40),
            new(start.AddMinutes(5), (from + to) / 2, 5.0, 80),
            new(start.AddMinutes(10), to, 5.0, 60),
        ];
        return new RecordedTrip(TripEdit.Summarize(fixes), odometerStart, odometerEnd);
    }

    private static TripPointInput Input(TripPoint p) => new(new DateTimeOffset(p.RecordedAt), p.Latitude, p.Longitude);

    private static TripPointInput Input(DateTime at, double lat) => new(new DateTimeOffset(at), lat, 5.0);

    private static List<TripPoint> Points(string json) => JsonSerializer.Deserialize<List<TripPoint>>(json)!;

    // The second trip, starting two minutes earlier from 1.1 km further back.
    private static async Task<TripEditResult> StartEarlierAsync(Setup s, CancellationToken ct)
    {
        var editor = (await s.Repo.GetEditorAsync(s.Vehicle.Id, s.ToWork, ct)).Editor!;
        return await s.Repo.SetPointsAsync(s.Vehicle.Id, s.ToWork,
            [Input(T0.AddMinutes(18), 52.09), .. editor.Points.Select(Input)], ct);
    }

    // ── What the editor is told ──────────────────────────────────────────────

    [Fact]
    public async Task Editor_NamesTheTripsEitherSideAsTheStopsAndTheLimits()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = await new Setup().SaveTwoAsync(ct);

        var first = (await s.Repo.GetEditorAsync(s.Vehicle.Id, s.ToFuel, ct)).Editor!;
        var second = (await s.Repo.GetEditorAsync(s.Vehicle.Id, s.ToWork, ct)).Editor!;

        Assert.Equal((null, T0.AddMinutes(20)), (first.NotBefore, first.NotAfter));
        Assert.Null(first.PreviousStop);
        Assert.Equal(new TripStop(52.10, 5.0, T0.AddMinutes(20), T0.AddMinutes(10)), first.NextStop);
        // The recording line bounds the last saved trip.
        Assert.Equal((T0.AddMinutes(10), T0.AddHours(1)), (second.NotBefore, second.NotAfter));
        Assert.Equal((52.10, T0.AddMinutes(10)), (second.PreviousStop!.Latitude, second.PreviousStop.RecordedAt));
        Assert.False(second.Suggestion);
        Assert.Null(second.OriginalPoints);
        Assert.Equal(3 + TripEdit.MaxAddedPoints, second.MaxPoints);
    }

    [Fact]
    public async Task Editor_PrefersTheCarsParkedFixes_AndSuggestsAParkingSpotAwayFromTheStart()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = await new Setup().SaveTwoAsync(ct);
        // Parked 560 m on from where the first trip's fixes end, as an old trip never recorded.
        await s.AddParkedFixAsync(T0.AddMinutes(11), 52.105, ct);

        var first = (await s.Repo.GetEditorAsync(s.Vehicle.Id, s.ToFuel, ct)).Editor!;
        var second = (await s.Repo.GetEditorAsync(s.Vehicle.Id, s.ToWork, ct)).Editor!;

        Assert.Equal(new TripStop(52.105, 5.0, T0.AddMinutes(11), T0.AddMinutes(11)), first.NextStop);
        Assert.Equal((52.105, T0.AddMinutes(11)), (second.PreviousStop!.Latitude, second.PreviousStop.RecordedAt));
        // No wake-up was reported, so it left 560 m at town speed before the first fix.
        Assert.InRange(second.PreviousStop.SuggestedAt, T0.AddMinutes(18.6), T0.AddMinutes(18.7));
        Assert.True(second.Suggestion);
    }

    [Fact]
    public async Task Editor_OnlyForTheVehiclesOwnReadableTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = await new Setup().SaveTwoAsync(ct);
        var row = await s.Db.Trips.SingleAsync(t => t.Id == s.ToWork, ct);
        row.PointsJson = "not json";
        await s.Db.SaveChangesAsync(ct);

        Assert.Equal(TripEditOutcome.NotFound, (await s.Repo.GetEditorAsync(s.Vehicle.Id + 1, s.ToFuel, ct)).Outcome);
        Assert.Equal(TripEditOutcome.Unreadable, (await s.Repo.GetEditorAsync(s.Vehicle.Id, s.ToWork, ct)).Outcome);
        Assert.Equal(TripEditOutcome.Unreadable, (await s.Repo.SetPointsAsync(s.Vehicle.Id, s.ToWork, [], ct)).Outcome);
    }

    // ── Saving ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_ReplacesTheRoute_AndEverythingReadOffIt_ButNotTheOdometer()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = await new Setup().SaveTwoAsync(ct);
        var before = await s.RowAsync(s.ToWork, ct);

        var result = await StartEarlierAsync(s, ct);

        Assert.Equal(TripEditOutcome.Ok, result.Outcome);
        var row = await s.RowAsync(s.ToWork, ct);
        Assert.Equal((T0.AddMinutes(18), 52.09, 4), (row.StartedAt, row.StartLatitude, row.PointCount));
        Assert.Equal(12.23, row.DistanceKm);
        Assert.Equal(TripPointSource.Manual, Points(row.PointsJson)[0].Source);
        Assert.Equal(before.PointsJson, row.OriginalPointsJson);
        Assert.NotNull(row.EditedAt);
        Assert.Equal((1011.0, 1022.0), (row.OdometerStartKm, row.OdometerEndKm));
        Assert.Equal(row.EditedAt, result.Editor!.EditedAt);
        Assert.Equal(3, result.Editor.OriginalPoints!.Count);
        Assert.True(Assert.Single(await s.Repo.GetLogEntriesAsync(s.Vehicle.Id, [s.ToWork], ct)).IsEdited);
    }

    [Fact]
    public async Task Save_KeepsThePointsAsRecordedFromTheFirstEdit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = await new Setup().SaveTwoAsync(ct);
        var recorded = (await s.RowAsync(s.ToWork, ct)).PointsJson;
        await StartEarlierAsync(s, ct);

        // A second edit drops the point the first one added.
        var current = Points((await s.RowAsync(s.ToWork, ct)).PointsJson);
        await s.Repo.SetPointsAsync(s.Vehicle.Id, s.ToWork, [.. current.Skip(1).Select(Input)], ct);

        var row = await s.RowAsync(s.ToWork, ct);
        Assert.Equal(recorded, row.OriginalPointsJson);
        Assert.Equal(recorded, row.PointsJson);
    }

    [Fact]
    public async Task Save_ClearsTheAddressOfAnEndThatMoved_AndKeepsTheOther()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = await new Setup().SaveTwoAsync(ct);
        var place = new PlaceAddress("Somewhere 1", "Somewhere", "1", "Town", null, "nl");
        await s.Repo.SavePlacesAsync(s.Vehicle.Id, [new TripPlaces(s.ToWork, place, place)], ct);

        await StartEarlierAsync(s, ct);

        var row = await s.RowAsync(s.ToWork, ct);
        Assert.Null(row.StartPlaceJson);
        Assert.NotNull(row.EndPlaceJson);
    }

    [Fact]
    public async Task Save_LabelsAPurposeNobodyChoseAgain_FromTheNewEnds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = await new Setup().SaveTwoAsync(ct);
        await s.HomeAndWorkAsync(ct);
        var editor = (await s.Repo.GetEditorAsync(s.Vehicle.Id, s.ToWork, ct)).Editor!;
        // Starting from home instead of the fuel stop: 11 km in eight minutes.
        TripPointInput[] fromHome = [Input(T0.AddMinutes(12), 52.00), .. editor.Points.Select(Input)];

        await s.Repo.SetPointsAsync(s.Vehicle.Id, s.ToWork, fromHome, ct);
        var automatic = (await s.RowAsync(s.ToWork, ct)).Purpose;
        await s.Repo.ResetPointsAsync(s.Vehicle.Id, s.ToWork, ct);
        await s.Repo.SetPurposeAndNotesAsync(s.Vehicle.Id, s.ToWork, TripPurpose.Private, null, ct);
        await s.Repo.SetPointsAsync(s.Vehicle.Id, s.ToWork, fromHome, ct);

        Assert.Equal(TripPurpose.Commute, automatic);
        Assert.Equal(TripPurpose.Private, (await s.RowAsync(s.ToWork, ct)).Purpose);
    }

    [Fact]
    public async Task Save_RefusesPointsRunningIntoTheTripsEitherSide_AndChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = await new Setup().SaveTwoAsync(ct);
        var editor = (await s.Repo.GetEditorAsync(s.Vehicle.Id, s.ToWork, ct)).Editor!;
        var before = await s.RowAsync(s.ToWork, ct);

        var intoPrevious = await s.Repo.SetPointsAsync(s.Vehicle.Id, s.ToWork,
            [Input(T0.AddMinutes(9), 52.10), .. editor.Points.Select(Input)], ct);
        var pastTheLine = await s.Repo.SetPointsAsync(s.Vehicle.Id, s.ToWork,
            [.. editor.Points.Select(Input), Input(T0.AddHours(2), 52.21)], ct);

        Assert.Equal((TripEditOutcome.Invalid, "trip.overlapsPrevious"), (intoPrevious.Outcome, intoPrevious.Problem!.Code));
        Assert.Equal((TripEditOutcome.Invalid, "trip.overlapsNext"), (pastTheLine.Outcome, pastTheLine.Problem!.Code));
        var after = await s.RowAsync(s.ToWork, ct);
        Assert.Equal((before.PointsJson, (string?)null, (DateTime?)null), (after.PointsJson, after.OriginalPointsJson, after.EditedAt));
    }

    // ── Reset ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reset_PutsTheRouteBackAsRecorded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = await new Setup().SaveTwoAsync(ct);
        var recorded = await s.RowAsync(s.ToWork, ct);
        await StartEarlierAsync(s, ct);

        var result = await s.Repo.ResetPointsAsync(s.Vehicle.Id, s.ToWork, ct);

        var row = await s.RowAsync(s.ToWork, ct);
        Assert.Equal(TripEditOutcome.Ok, result.Outcome);
        Assert.Null(result.Editor!.OriginalPoints);
        Assert.Equal(recorded.PointsJson, row.PointsJson);
        Assert.Equal((recorded.StartedAt, recorded.StartLatitude, recorded.PointCount), (row.StartedAt, row.StartLatitude, row.PointCount));
        Assert.Equal(recorded.DistanceKm, row.DistanceKm, precision: 6);
        Assert.Equal(((string?)null, (DateTime?)null), (row.OriginalPointsJson, row.EditedAt));
        Assert.False(Assert.Single(await s.Repo.GetLogEntriesAsync(s.Vehicle.Id, [s.ToWork], ct)).IsEdited);
    }

    [Fact]
    public async Task Reset_OfATripNobodyEdited_ChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = await new Setup().SaveTwoAsync(ct);
        var before = await s.RowAsync(s.ToWork, ct);

        var result = await s.Repo.ResetPointsAsync(s.Vehicle.Id, s.ToWork, ct);

        Assert.Equal(TripEditOutcome.Ok, result.Outcome);
        Assert.Equal(before.PointsJson, (await s.RowAsync(s.ToWork, ct)).PointsJson);
        Assert.Equal(TripEditOutcome.NotFound, (await s.Repo.ResetPointsAsync(s.Vehicle.Id, s.ToWork + 100, ct)).Outcome);
    }

    // ── Joining an edited trip ───────────────────────────────────────────────

    [Fact]
    public async Task Join_KeepsBothTripsAsRecorded_SoAResetKeepsTheJoin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = await new Setup().SaveTwoAsync(ct);
        var recordedFirst = Points((await s.RowAsync(s.ToFuel, ct)).PointsJson);
        var recordedSecond = Points((await s.RowAsync(s.ToWork, ct)).PointsJson);
        await StartEarlierAsync(s, ct);

        await s.Repo.MergeWithNextAsync(s.Vehicle.Id, s.ToFuel, ct);

        var joined = await s.RowAsync(s.ToFuel, ct);
        Assert.Equal(7, joined.PointCount);
        Assert.Equal([.. recordedFirst, .. recordedSecond], Points(joined.OriginalPointsJson!));
        Assert.NotNull(joined.EditedAt);

        await s.Repo.ResetPointsAsync(s.Vehicle.Id, s.ToFuel, ct);
        Assert.Equal(6, (await s.RowAsync(s.ToFuel, ct)).PointCount);
    }

    [Fact]
    public async Task Join_OfTwoTripsNobodyEdited_KeepsNoCopy()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var s = await new Setup().SaveTwoAsync(ct);

        await s.Repo.MergeWithNextAsync(s.Vehicle.Id, s.ToFuel, ct);

        Assert.Null((await s.RowAsync(s.ToFuel, ct)).OriginalPointsJson);
    }

    // ── The demo ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Demo_EditsARouteInMemory_AndResetsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var demo = new DemoTripRepository();
        var log = await demo.GetLogAsync(1, DateTime.UtcNow.AddDays(-60), DateTime.UtcNow, ct);
        // Amsterdam to Almere, which the demo has leave 8 km from where the trip before it ended.
        var entry = log.Single(e => e.Id == 5);
        var editor = (await demo.GetEditorAsync(1, entry.Id, ct)).Editor!;
        var stop = editor.PreviousStop!;

        var saved = await demo.SetPointsAsync(1, entry.Id,
            [new(new DateTimeOffset(stop.SuggestedAt), stop.Latitude, stop.Longitude), .. editor.Points.Select(Input)], ct);
        var edited = Assert.Single(await demo.GetLogEntriesAsync(1, [entry.Id], ct));
        var drawn = (await demo.GetTripsAsync(1, DateTime.UtcNow.AddDays(-60), DateTime.UtcNow, ct)).Single(t => t.Id == entry.Id);
        await demo.ResetPointsAsync(1, entry.Id, ct);
        var reset = Assert.Single(await demo.GetLogEntriesAsync(1, [entry.Id], ct));

        Assert.True(editor.Suggestion);
        Assert.Equal(TripEditOutcome.Ok, saved.Outcome);
        Assert.Equal(editor.Points, saved.Editor!.OriginalPoints);
        Assert.True(edited.IsEdited);
        Assert.Equal((stop.Latitude, stop.SuggestedAt), (edited.StartLatitude, edited.StartedAt));
        Assert.True(edited.DistanceKm > entry.DistanceKm + 7);
        Assert.Equal(TripPointSource.Manual, drawn.Points[0].Source);
        Assert.False(reset.IsEdited);
        Assert.Equal((entry.StartedAt, entry.StartLatitude), (reset.StartedAt, reset.StartLatitude));
    }

    [Fact]
    public async Task Demo_RefusesWhatTheRealRepositoryRefuses()
    {
        var ct = TestContext.Current.CancellationToken;
        var demo = new DemoTripRepository();
        var editor = (await demo.GetEditorAsync(1, 5, ct)).Editor!;

        var one = await demo.SetPointsAsync(1, 5, [Input(editor.Points[0])], ct);

        Assert.Equal("trip.pointCount", one.Problem!.Code);
        Assert.Equal(TripEditOutcome.NotFound, (await demo.GetEditorAsync(1, 999, ct)).Outcome);
    }
}
