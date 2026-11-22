using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

public class PhonePositionTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private const string Tracker = "fake-device-0001";

    private static PhonePositionInput At(DateTime at, double latitude = 52, double longitude = 5,
        double? accuracy = 5, double? speed = 40) =>
        new(new DateTimeOffset(at), latitude, longitude, accuracy, speed);

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Theory]
    [InlineData("fake-device-0001", true)]
    [InlineData("0f8c6a2e-4b1d-4c7e-9a55-2d3b1e6f7a80", true)]
    [InlineData("short", false)]
    [InlineData("fake device 0001", false)]
    [InlineData("fake:device:0001", false)]
    [InlineData(null, false)]
    public void TrackerIds_AreTheAppsOwnRandomIds(string? id, bool valid) =>
        Assert.Equal(valid, PhonePositionBatch.IsTrackerId(id));

    [Fact]
    public void TrackerIds_HaveAMaximumLength() =>
        Assert.False(PhonePositionBatch.IsTrackerId(new string('a', PhonePosition.MaxTrackerLength + 1)));

    [Fact]
    public void ABatch_IsStoredOncePerMoment_OldestFirst()
    {
        var parsed = PhonePositionBatch.Parse(1, Tracker,
            [At(Now.AddMinutes(-1)), At(Now.AddMinutes(-2)), At(Now.AddMinutes(-1), latitude: 53)], Now)!;

        Assert.Equal([Now.AddMinutes(-2), Now.AddMinutes(-1)], parsed.Select(p => p.RecordedAt));
        Assert.All(parsed, p => Assert.Equal((1, Tracker), (p.VehicleId, p.TrackerId)));
    }

    [Fact]
    public void PositionsOutsideTheWindow_AreLeftOut_WithoutRefusingTheRest()
    {
        var parsed = PhonePositionBatch.Parse(1, Tracker,
            [At(Now - PhonePositionBatch.MaxAge - TimeSpan.FromMinutes(1)), At(Now), At(Now.AddMinutes(10))], Now)!;

        Assert.Equal([Now], parsed.Select(p => p.RecordedAt));
    }

    public static TheoryData<PhonePositionInput> Malformed => new()
    {
        At(Now, latitude: 91),
        At(Now, longitude: -181),
        At(Now, latitude: double.NaN),
        At(Now, accuracy: -1),
        At(Now, accuracy: double.PositiveInfinity),
        At(Now, speed: -1),
        At(Now, speed: PhonePositionBatch.MaxSpeedKmh + 1),
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedPosition_RefusesTheBatch(PhonePositionInput position) =>
        Assert.Null(PhonePositionBatch.Parse(1, Tracker, [At(Now.AddMinutes(-1)), position], Now));

    [Fact]
    public void ABatchThatIsTooLargeOrMissing_IsRefused()
    {
        Assert.Null(PhonePositionBatch.Parse(1, Tracker, null, Now));
        Assert.Null(PhonePositionBatch.Parse(1, Tracker,
            [.. Enumerable.Range(0, PhonePositionBatch.MaxPositions + 1).Select(i => At(Now.AddSeconds(-i)))], Now));
    }

    [Fact]
    public void APosition_IsAFixOfTheAppsOwnTracker()
    {
        var fix = new PhonePosition { TrackerId = Tracker, RecordedAt = Now, Latitude = 52, Longitude = 5, AccuracyM = 5 }.ToFix();

        Assert.Equal(new PhoneFix(Now, 52, 5, 5, null, "app:" + Tracker), fix);
    }

    [Fact]
    public async Task ABatchSentAgain_StoresNothingNew()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.Vehicles.Add(new() { Id = 1, Vin = "FAKEVN00000000001" });
        await db.SaveChangesAsync(ct);
        var repo = new PhonePositionRepository(db);
        var batch = PhonePositionBatch.Parse(1, Tracker, [At(Now.AddMinutes(-2)), At(Now.AddMinutes(-1))], Now)!;

        Assert.Equal(2, await repo.AddAsync(1, Tracker, batch, ct));
        Assert.Equal(0, await repo.AddAsync(1, Tracker, batch, ct));
        // The same moment from another device is another position.
        Assert.Equal(2, await repo.AddAsync(1, "fake-device-0002", batch, ct));
        Assert.Equal(4, await db.PhonePositions.CountAsync(ct));
    }

    [Fact]
    public async Task LatePositions_QueueATripThatFoundNoPhone_ButLeaveTheOthers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.Vehicles.Add(new() { Id = 1, Vin = "FAKEVN00000000001" });
        Trip Trip(int hour, PhoneFusionOutcome? fusion, bool edited = false) => new()
        {
            VehicleId = 1,
            StartedAt = Now.AddHours(-hour),
            EndedAt = Now.AddHours(-hour).AddMinutes(20),
            PointsJson = "[]",
            PhoneFusion = fusion,
            EditedAt = edited ? Now : null,
        };
        db.Trips.AddRange(
            Trip(5, PhoneFusionOutcome.NoMatch),
            Trip(4, PhoneFusionOutcome.Fused),
            Trip(3, PhoneFusionOutcome.NoMatch, edited: true),
            // Found no phone, but the batch has no position inside it.
            Trip(2, PhoneFusionOutcome.NoMatch));
        await db.SaveChangesAsync(ct);
        var batch = PhonePositionBatch.Parse(1, Tracker,
            [.. new[] { 5, 4, 3 }.Select(h => At(Now.AddHours(-h).AddMinutes(10))), At(Now.AddHours(-1))], Now)!;

        await new PhonePositionRepository(db).AddAsync(1, Tracker, batch, ct);

        Assert.Equal(
            [PhoneFusionOutcome.Pending, PhoneFusionOutcome.Fused, PhoneFusionOutcome.NoMatch, PhoneFusionOutcome.NoMatch],
            await db.Trips.OrderBy(t => t.StartedAt).Select(t => t.PhoneFusion).ToListAsync(ct));
    }

    [Fact]
    public async Task Fixes_AreTheVehiclesPositionsInTheWindow_AndOldOnesCanBeRemoved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.Vehicles.AddRange(new Vehicle { Id = 1, Vin = "FAKEVN00000000001" }, new Vehicle { Id = 2, Vin = "FAKEVN00000000002" });
        await db.SaveChangesAsync(ct);
        var repo = new PhonePositionRepository(db);
        var batch = new[] { -40, -20, -10 }.Select(m => At(Now.AddMinutes(m))).ToArray();
        await repo.AddAsync(1, Tracker, PhonePositionBatch.Parse(1, Tracker, batch, Now)!, ct);
        await repo.AddAsync(2, Tracker, PhonePositionBatch.Parse(2, Tracker, batch, Now)!, ct);

        var fixes = await repo.GetFixesAsync(1, Now.AddMinutes(-30), Now, ct);
        Assert.Equal([Now.AddMinutes(-20), Now.AddMinutes(-10)], fixes.Select(f => f.RecordedAt));

        Assert.Equal(2, await repo.RemoveOlderThanAsync(Now.AddMinutes(-30), ct));
        Assert.Equal(4, (await repo.ListAsync(1, ct)).Count + (await repo.ListAsync(2, ct)).Count);
    }
}
