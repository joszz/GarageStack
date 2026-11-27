using GarageStack.Api.Endpoints;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

// The frontend re-registers its push subscription on every load (usePush.initPushState), not only
// when the user opts in, so this path runs constantly and must never create a second row for an
// endpoint or drop keys that changed.
public class PushSubscriptionUpsertTests
{
    private const string Endpoint = "https://push.example/abc";

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task InsertsASubscriptionTheServerHasNotSeen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();

        await PushEndpoints.UpsertSubscriptionAsync(new PushSubscriptionRepository(db), new PushSubscribeRequest(Endpoint, "p256dh", "auth"), ct);

        var stored = await db.PushSubscriptions.SingleAsync(ct);
        Assert.Equal(Endpoint, stored.Endpoint);
        Assert.Equal("p256dh", stored.P256DhKey);
        Assert.Equal("auth", stored.AuthKey);
    }

    [Fact]
    public async Task ReRegisteringTheSameSubscriptionDoesNotAddASecondRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var req = new PushSubscribeRequest(Endpoint, "p256dh", "auth");

        await PushEndpoints.UpsertSubscriptionAsync(new PushSubscriptionRepository(db), req, ct);
        await PushEndpoints.UpsertSubscriptionAsync(new PushSubscriptionRepository(db), req, ct);
        await PushEndpoints.UpsertSubscriptionAsync(new PushSubscriptionRepository(db), req, ct);

        Assert.Equal(1, await db.PushSubscriptions.CountAsync(ct));
    }

    [Fact]
    public async Task UpdatesTheKeysWhenTheBrowserRotatedThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.PushSubscriptions.Add(new PushSubscription
        {
            Endpoint = Endpoint,
            P256DhKey = "old-p256dh",
            AuthKey = "old-auth",
        });
        await db.SaveChangesAsync(ct);

        await PushEndpoints.UpsertSubscriptionAsync(new PushSubscriptionRepository(db), new PushSubscribeRequest(Endpoint, "new-p256dh", "new-auth"), ct);

        var stored = await db.PushSubscriptions.SingleAsync(ct);
        Assert.Equal("new-p256dh", stored.P256DhKey);
        Assert.Equal("new-auth", stored.AuthKey);
    }

    [Fact]
    public async Task LeavesOtherEndpointsAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.PushSubscriptions.Add(new PushSubscription
        {
            Endpoint = "https://push.example/other",
            P256DhKey = "other-p256dh",
            AuthKey = "other-auth",
        });
        await db.SaveChangesAsync(ct);

        await PushEndpoints.UpsertSubscriptionAsync(new PushSubscriptionRepository(db), new PushSubscribeRequest(Endpoint, "p256dh", "auth"), ct);

        Assert.Equal(2, await db.PushSubscriptions.CountAsync(ct));
        var other = await db.PushSubscriptions
            .SingleAsync(s => s.Endpoint == "https://push.example/other", ct);
        Assert.Equal("other-p256dh", other.P256DhKey);
    }

    // ── The types each device switched off ─────────────────────────────────────

    [Fact]
    public async Task StoresTheTypesTheDeviceSwitchedOff_KnownOnesOnlyAndInOneOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();

        await PushEndpoints.UpsertSubscriptionAsync(new PushSubscriptionRepository(db), new PushSubscribeRequest(Endpoint, "p256dh", "auth", ["low-tyre", "engine-start", "not-a-type", "low-tyre"]), ct);

        var stored = await db.PushSubscriptions.SingleAsync(ct);
        Assert.Equal(["engine-start", "low-tyre"], stored.ExcludedCategories);
    }

    [Fact]
    public async Task UpdatesTheTypesWhenTheDeviceChangedThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        await PushEndpoints.UpsertSubscriptionAsync(new PushSubscriptionRepository(db), new PushSubscribeRequest(Endpoint, "p256dh", "auth", ["engine-start"]), ct);

        await PushEndpoints.UpsertSubscriptionAsync(new PushSubscriptionRepository(db), new PushSubscribeRequest(Endpoint, "p256dh", "auth", ["maintenance"]), ct);

        var stored = await db.PushSubscriptions.SingleAsync(ct);
        Assert.Equal(["maintenance"], stored.ExcludedCategories);
    }

    // A page that predates the list sends none: that says nothing about the device's choice.
    [Fact]
    public async Task ARequestWithoutTheList_KeepsTheStoredTypes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        await PushEndpoints.UpsertSubscriptionAsync(new PushSubscriptionRepository(db), new PushSubscribeRequest(Endpoint, "p256dh", "auth", ["engine-start"]), ct);

        await PushEndpoints.UpsertSubscriptionAsync(new PushSubscriptionRepository(db), new PushSubscribeRequest(Endpoint, "p256dh", "auth"), ct);

        var stored = await db.PushSubscriptions.SingleAsync(ct);
        Assert.Equal(["engine-start"], stored.ExcludedCategories);
    }

    // ── The types each device switched off for one car ─────────────────────────

    [Fact]
    public async Task StoresTheTypesSwitchedOffForOneCar_KnownOnesOnlyAndInOneOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var byVehicle = new Dictionary<int, IReadOnlyList<string>>
        {
            [4] = ["low-tyre", "engine-start", "not-a-type", "low-tyre"],
            [2] = ["not-a-type"],
        };

        await PushEndpoints.UpsertSubscriptionAsync(new PushSubscriptionRepository(db),
            new PushSubscribeRequest(Endpoint, "p256dh", "auth", ExcludedCategoriesByVehicle: byVehicle), ct);

        var stored = await db.PushSubscriptions.SingleAsync(ct);
        Assert.Equal([4], stored.ExcludedCategoriesByVehicle.Keys);
        Assert.Equal(["engine-start", "low-tyre"], stored.ExcludedCategoriesByVehicle[4]);
        Assert.Empty(stored.ExcludedCategories);
    }

    [Fact]
    public async Task UpdatesTheTypesForACarWhenTheDeviceChangedThem_AndClearsACarWithNoneOff()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var repo = new PushSubscriptionRepository(db);
        await PushEndpoints.UpsertSubscriptionAsync(repo, new PushSubscribeRequest(Endpoint, "p256dh", "auth",
            ExcludedCategoriesByVehicle: new Dictionary<int, IReadOnlyList<string>> { [1] = ["low-tyre"], [2] = ["engine-start"] }), ct);

        await PushEndpoints.UpsertSubscriptionAsync(repo, new PushSubscribeRequest(Endpoint, "p256dh", "auth",
            ExcludedCategoriesByVehicle: new Dictionary<int, IReadOnlyList<string>> { [1] = ["maintenance"] }), ct);

        var stored = await db.PushSubscriptions.SingleAsync(ct);
        Assert.Equal([1], stored.ExcludedCategoriesByVehicle.Keys);
        Assert.Equal(["maintenance"], stored.ExcludedCategoriesByVehicle[1]);
    }

    // An empty map is a choice (every car back to the device's own list); a missing one is not.
    [Fact]
    public async Task AnEmptyMapClearsTheCarsTypes_AndAMissingOneKeepsThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var repo = new PushSubscriptionRepository(db);
        await PushEndpoints.UpsertSubscriptionAsync(repo, new PushSubscribeRequest(Endpoint, "p256dh", "auth",
            ExcludedCategoriesByVehicle: new Dictionary<int, IReadOnlyList<string>> { [1] = ["low-tyre"] }), ct);

        await PushEndpoints.UpsertSubscriptionAsync(repo, new PushSubscribeRequest(Endpoint, "p256dh", "auth", ["engine-start"]), ct);
        Assert.Equal(["low-tyre"], (await db.PushSubscriptions.SingleAsync(ct)).ExcludedCategoriesByVehicle[1]);

        await PushEndpoints.UpsertSubscriptionAsync(repo, new PushSubscribeRequest(Endpoint, "p256dh", "auth",
            ExcludedCategoriesByVehicle: new Dictionary<int, IReadOnlyList<string>>()), ct);
        var stored = await db.PushSubscriptions.SingleAsync(ct);
        Assert.Empty(stored.ExcludedCategoriesByVehicle);
        Assert.Equal(["engine-start"], stored.ExcludedCategories);
    }

    // The page registers its subscription on every load: the same choice listed in another order
    // must not look like a change.
    [Fact]
    public async Task TheSameChoiceInAnotherOrder_IsNotAChange()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var repo = new PushSubscriptionRepository(db);
        await PushEndpoints.UpsertSubscriptionAsync(repo, new PushSubscribeRequest(Endpoint, "p256dh", "auth",
            ExcludedCategoriesByVehicle: new Dictionary<int, IReadOnlyList<string>> { [1] = ["low-tyre", "engine-start"] }), ct);
        db.ChangeTracker.Clear();

        await PushEndpoints.UpsertSubscriptionAsync(repo, new PushSubscribeRequest(Endpoint, "p256dh", "auth",
            ExcludedCategoriesByVehicle: new Dictionary<int, IReadOnlyList<string>> { [1] = ["engine-start", "low-tyre"] }), ct);

        Assert.DoesNotContain(db.ChangeTracker.Entries(), e => e.State != EntityState.Unchanged);
        Assert.Equal(["engine-start", "low-tyre"], (await db.PushSubscriptions.SingleAsync(ct)).ExcludedCategoriesByVehicle[1]);
    }

    // The Worker drops the subscriptions a push service reported gone; others stay, and an
    // endpoint that was never stored is no error.
    [Fact]
    public async Task Remove_DropsOnlyTheNamedEndpoints()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var repo = new PushSubscriptionRepository(db);
        await repo.UpsertAsync(Endpoint, "p256dh", "auth", null, null, null, ct);
        await repo.UpsertAsync("https://push.example/other", "p256dh", "auth", null, null, null, ct);

        await repo.RemoveAsync([Endpoint, "https://push.example/unknown"], ct);

        Assert.Equal("https://push.example/other", Assert.Single(await repo.ListAsync(ct)).Endpoint);
    }

    // Only the opt-in types count as switched on; anything else in the list is not one.
    [Fact]
    public async Task StoresTheOptInTypesSwitchedOn_AndKeepsThemWhenTheListIsLeftOut()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var repo = new PushSubscriptionRepository(db);

        await PushEndpoints.UpsertSubscriptionAsync(repo,
            new PushSubscribeRequest(Endpoint, "p256dh", "auth", IncludedCategories: ["weekly-summary", "low-tyre", "bogus"]), ct);
        await PushEndpoints.UpsertSubscriptionAsync(repo, new PushSubscribeRequest(Endpoint, "p256dh", "auth"), ct);

        Assert.Equal(["weekly-summary"], (await db.PushSubscriptions.AsNoTracking().SingleAsync(ct)).IncludedCategories);
    }
}
