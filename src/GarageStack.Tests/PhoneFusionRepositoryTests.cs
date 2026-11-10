using System.Text.Json;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

public class PhoneFusionRepositoryTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TripPoint[] Points = [new(Start, 52, 5, 40), new(Start.AddMinutes(2), 52.01, 5, 40)];
    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static PhoneFusionRepository Phones(AppDbContext db) =>
        new(db, new TelemetryRepository(db), NullLogger<PhoneFusionRepository>.Instance);

    private static async Task<long> SeedAsync(AppDbContext db, CancellationToken ct)
    {
        db.Vehicles.Add(new() { Id = 1, Vin = "FAKEVN00000000001" });
        await db.SaveChangesAsync(ct);
        await new TripRepository(db, new TelemetryRepository(db))
            .SaveRecordedAsync(1, [new(TripEdit.Summarize(Points), 100, 102)], Start.AddHours(1), ct);
        return await db.Trips.Select(t => t.Id).SingleAsync(ct);
    }

    [Fact]
    public async Task RespectsLinkDateDelayAndExplicitBackfill()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var id = await SeedAsync(db, ct);
        var repo = Phones(db);
        await repo.SetAsync(1, ["fake-phone"], Start.AddDays(1), ct);
        Assert.Empty(await repo.PendingAsync(Start.AddDays(2), 10, ct));
        await repo.RequestBackfillAsync(1, Start.AddDays(-1), Start.AddDays(1), ct);
        await repo.QueueBackfillAsync(ct);
        Assert.Empty(await repo.PendingAsync(Start.AddMinutes(1), 10, ct));
        Assert.Equal([id], await repo.PendingAsync(Start.AddDays(2), 10, ct));
        Assert.Empty(await db.PhoneBackfillRequests.ToListAsync(ct));
    }

    [Fact]
    public async Task FusionIsAsRecordedAndEditorResetRestoresPhonePoints()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var id = await SeedAsync(db, ct);
        var repo = Phones(db);
        await repo.SetAsync(1, ["fake-phone"], Start.AddDays(-1), ct);
        var work = (await repo.ReadWorkAsync(id, ct))!;
        TripPoint[] fused = [Points[0], new(Start.AddMinutes(1), 52.005, 5, 40, TripPointSource.Phone), Points[1]];
        await repo.CompleteAsync(work, new(fused, PhoneFusionOutcome.Fused), Start.AddHours(1), ct);
        var row = await db.Trips.AsNoTracking().SingleAsync(ct);
        Assert.Null(row.EditedAt);
        Assert.Null(row.OriginalPointsJson);
        Assert.Equal(3, row.PointCount);
        var trips = new TripRepository(db, new TelemetryRepository(db));
        await trips.SetPointsAsync(1, id, Points.Select(p => new TripPointInput(new(p.RecordedAt), p.Latitude, p.Longitude)).ToArray(), ct);
        await trips.ResetPointsAsync(1, id, ct);
        db.ChangeTracker.Clear();
        Assert.Contains(JsonSerializer.Deserialize<List<TripPoint>>((await db.Trips.SingleAsync(ct)).PointsJson)!,
            p => p.Source == TripPointSource.Phone);
    }

    [Fact]
    public async Task CompletedWorkCannotBeReprocessedByAConcurrentPass()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var id = await SeedAsync(db, ct);
        var repo = Phones(db);
        await repo.SetAsync(1, ["fake-phone"], Start.AddDays(-1), ct);
        var work = (await repo.ReadWorkAsync(id, ct))!;
        await repo.CompleteAsync(work, new(Points, PhoneFusionOutcome.NoMatch), Start, ct);
        Assert.Null(await repo.ReadWorkAsync(id, ct));
        Assert.Null(await repo.CompleteAsync(work, new(Points, PhoneFusionOutcome.Failed), Start, ct));
        Assert.Equal(PhoneFusionOutcome.NoMatch, (await db.Trips.SingleAsync(ct)).PhoneFusion);
    }

    [Fact]
    public async Task RecoveryWindowUsesCarDepartureNotAnEntireParkedDay()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var id = await SeedAsync(db, ct);
        db.TelemetrySnapshots.Add(new()
        {
            VehicleId = 1,
            RecordedAt = Start.AddDays(-1),
            Latitude = 51.99,
            Longitude = 5,
            Speed = 0,
        });
        await db.SaveChangesAsync(ct);
        var repo = Phones(db);
        await repo.SetAsync(1, ["fake-phone"], Start.AddDays(-2), ct);
        var work = (await repo.ReadWorkAsync(id, ct))!;
        Assert.NotNull(work.Parked);
        Assert.True(work.Parked.RecordedAt > Start.AddMinutes(-5));
        Assert.Equal(51.99, work.Parked.Latitude);
    }

    [Fact]
    public async Task RemovingAndRelinkingDuringFetchDoesNotRestoreConsentForOldTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var id = await SeedAsync(db, ct);
        var repo = Phones(db);
        await repo.SetAsync(1, ["fake-phone"], Start.AddDays(-1), ct);
        var work = (await repo.ReadWorkAsync(id, ct))!;
        await repo.SetAsync(1, [], Start.AddDays(1), ct);
        await repo.SetAsync(1, ["fake-phone"], Start.AddDays(1), ct);
        await repo.CompleteAsync(work, new(Points, PhoneFusionOutcome.Fused), Start.AddDays(1), ct);
        Assert.Equal(PhoneFusionOutcome.NotLinked, (await db.Trips.SingleAsync(ct)).PhoneFusion);
    }

    [Fact]
    public async Task JoiningAFusedTripDoesNotScheduleAnotherFusion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var id = await SeedAsync(db, ct);
        var trips = new TripRepository(db, new TelemetryRepository(db));
        var later = Points.Select(p => p with { RecordedAt = p.RecordedAt.AddHours(2) }).ToArray();
        await trips.SaveRecordedAsync(1, [new(TripEdit.Summarize(later), 102, 104)], Start.AddHours(3), ct);
        var second = await db.Trips.SingleAsync(t => t.Id != id, ct);
        second.PhoneFusion = PhoneFusionOutcome.Fused;
        second.PhoneFusionAt = Start.AddHours(3);
        await db.SaveChangesAsync(ct);
        await trips.MergeWithNextAsync(1, id, ct);
        Assert.Equal(PhoneFusionOutcome.Fused, (await db.Trips.SingleAsync(ct)).PhoneFusion);
        Assert.Equal(Start.AddHours(3), (await db.Trips.SingleAsync(ct)).PhoneFusionAt);
    }

    [Fact]
    public async Task DoesNotOverwriteEditsOrUnlinkedTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var id = await SeedAsync(db, ct);
        var repo = Phones(db);
        await repo.SetAsync(1, ["fake-phone"], Start.AddDays(-1), ct);
        var work = (await repo.ReadWorkAsync(id, ct))!;
        await repo.SetAsync(1, [], Start, ct);
        await repo.CompleteAsync(work, new(Points, PhoneFusionOutcome.Fused), Start, ct);
        Assert.Equal(PhoneFusionOutcome.NotLinked, (await db.Trips.SingleAsync(ct)).PhoneFusion);
        await repo.SetAsync(1, ["fake-phone"], Start.AddDays(-1), ct);
        var trip = await db.Trips.SingleAsync(ct);
        trip.EditedAt = Start;
        trip.PhoneFusion = PhoneFusionOutcome.Pending;
        await db.SaveChangesAsync(ct);
        Assert.Empty(await repo.PendingAsync(Start.AddDays(1), 10, ct));
        await repo.CompleteAsync(work, new(Points, PhoneFusionOutcome.Fused), Start, ct);
        Assert.Equal(PhoneFusionOutcome.Pending, (await db.Trips.SingleAsync(ct)).PhoneFusion);
    }
}
