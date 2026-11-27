using GarageStack.Api.Endpoints;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GarageStack.Tests;

// What a device switched off for one car is a jsonb column with a converter, which the in-memory
// database does not exercise: its type, its default for the rows that existed before it, and the
// change tracking that decides whether a registration writes at all.
public class PushSubscriptionPostgresTests
{
    private const string Endpoint = "https://push.example/abc";

    [Fact]
    public async Task TheTypesForACar_SurviveARoundTripThroughJsonb()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using (var db = database.CreateContext())
        {
            await PushEndpoints.UpsertSubscriptionAsync(new PushSubscriptionRepository(db), new PushSubscribeRequest(Endpoint, "p256dh", "auth",
                ["maintenance"], null, new Dictionary<int, IReadOnlyList<string>> { [12] = ["low-tyre", "engine-start"], [3] = ["rdw"] }), ct);
        }

        await using var check = database.CreateContext();
        var stored = Assert.Single(await new PushSubscriptionRepository(check).ListAsync(ct));
        Assert.Equal([3, 12], stored.ExcludedCategoriesByVehicle.Keys.Order());
        Assert.Equal(["engine-start", "low-tyre"], stored.ExcludedCategoriesByVehicle[12]);
        Assert.Equal(["rdw"], stored.ExcludedCategoriesFor(3));
        Assert.Equal(["maintenance"], stored.ExcludedCategories);
    }

    [Fact]
    public async Task ChangingACarsTypes_IsWrittenAndTheSameChoiceIsNot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        var repo = new PushSubscriptionRepository(db);
        PushSubscribeRequest Req(params string[] types) => new(Endpoint, "p256dh", "auth",
            ExcludedCategoriesByVehicle: new Dictionary<int, IReadOnlyList<string>> { [1] = types });
        await PushEndpoints.UpsertSubscriptionAsync(repo, Req("low-tyre"), ct);

        await PushEndpoints.UpsertSubscriptionAsync(repo, Req("low-tyre"), ct);
        Assert.DoesNotContain(db.ChangeTracker.Entries(), e => e.State != EntityState.Unchanged);

        await PushEndpoints.UpsertSubscriptionAsync(repo, Req("low-tyre", "low-fuel"), ct);

        await using var check = database.CreateContext();
        Assert.Equal(["low-fuel", "low-tyre"], Assert.Single(await new PushSubscriptionRepository(check).ListAsync(ct)).ExcludedCategoriesByVehicle[1]);
    }

    // A subscription from before the column wants every type for every car, and reads as that.
    [Fact]
    public async Task ASubscriptionWithoutTheColumnsValue_HasNoTypesOffForAnyCar()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgresDatabase.CreateAsync(ct);
        await using (var db = database.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"PushSubscriptions\" (\"Endpoint\", \"P256DhKey\", \"AuthKey\", \"CreatedAt\") VALUES ({0}, 'k', 'a', now())",
                [Endpoint], ct);
        }

        await using var check = database.CreateContext();
        var stored = Assert.Single(await new PushSubscriptionRepository(check).ListAsync(ct));
        Assert.Empty(stored.ExcludedCategoriesByVehicle);
        Assert.Null(stored.ExcludedCategoriesFor(1));
    }
}
