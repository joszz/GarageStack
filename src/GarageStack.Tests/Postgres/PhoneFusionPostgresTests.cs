using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

public class PhoneFusionPostgresTests
{
    [Fact]
    public async Task BackfillFusionAndUnlinkUseMigratedSchemaAndRealQueries()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var car = new Vehicle { Vin = "FAKEVN00000000001" };
        db.Vehicles.Add(car);
        await db.SaveChangesAsync(ct);
        TripPoint[] points = [new(start, 52, 5, 40), new(start.AddMinutes(2), 52.01, 5, 40)];
        var telemetry = new TelemetryRepository(db);
        await new TripRepository(db, telemetry).SaveRecordedAsync(car.Id,
            [new(TripEdit.Summarize(points), 100, 102)], start.AddHours(1), ct);
        var repo = new PhoneFusionRepository(db, telemetry, NullLogger<PhoneFusionRepository>.Instance);
        await repo.SetAsync(car.Id, ["fake-phone"], start.AddDays(1), ct);
        Assert.Empty(await repo.PendingAsync(start.AddDays(2), 10, ct));
        await repo.RequestBackfillAsync(car.Id, start.AddDays(-1), start.AddDays(1), ct);
        await repo.QueueBackfillAsync(ct);
        var id = Assert.Single(await repo.PendingAsync(start.AddDays(2), 10, ct));
        var work = (await repo.ReadWorkAsync(id, ct))!;
        TripPoint[] fused = [points[0], new(start.AddMinutes(1), 52.005, 5, 40, TripPointSource.Phone), points[1]];
        await repo.CompleteAsync(work, new(fused, PhoneFusionOutcome.Fused), start.AddHours(2), ct);
        var trip = await db.Trips.AsNoTracking().SingleAsync(ct);
        Assert.Equal(PhoneFusionOutcome.Fused, trip.PhoneFusion);
        Assert.Equal(3, trip.PointCount);
        Assert.Null(trip.EditedAt);
        Assert.Null(trip.OriginalPointsJson);
        await repo.SetAsync(car.Id, [], start.AddDays(1), ct);
        Assert.Empty(await repo.ListAsync(car.Id, ct));
        Assert.Empty(await repo.PendingAsync(start.AddDays(2), 10, ct));
    }
}
