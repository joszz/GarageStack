using GarageStack.Api.Endpoints;
using GarageStack.Core.Models;
using GarageStack.Data;
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

        await PushEndpoints.UpsertSubscriptionAsync(
            db, new PushSubscribeRequest(Endpoint, "p256dh", "auth"), ct);

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

        await PushEndpoints.UpsertSubscriptionAsync(db, req, ct);
        await PushEndpoints.UpsertSubscriptionAsync(db, req, ct);
        await PushEndpoints.UpsertSubscriptionAsync(db, req, ct);

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

        await PushEndpoints.UpsertSubscriptionAsync(
            db, new PushSubscribeRequest(Endpoint, "new-p256dh", "new-auth"), ct);

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

        await PushEndpoints.UpsertSubscriptionAsync(
            db, new PushSubscribeRequest(Endpoint, "p256dh", "auth"), ct);

        Assert.Equal(2, await db.PushSubscriptions.CountAsync(ct));
        var other = await db.PushSubscriptions
            .SingleAsync(s => s.Endpoint == "https://push.example/other", ct);
        Assert.Equal("other-p256dh", other.P256DhKey);
    }
}
