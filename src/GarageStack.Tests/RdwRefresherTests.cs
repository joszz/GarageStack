using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

// One lookup, saved: what is asked, and what is kept when the RDW cannot be asked.
public class RdwRefresherTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private static readonly RdwOptions Enabled = new() { Enabled = true };

    [Fact]
    public async Task FirstLookup_SavesTheRegistrationAndEveryRecall_WithItsDetails()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = new FakeRdwClient
        {
            Statuses = [new("MGP240278", "O"), new("MGP260056", "P")],
            Details = [new("MGP240278", "defect", "risk", "repair", "GEM"), new("MGP260056", "d", "r", "f", "ERN")],
        };
        await using var services = RdwTestServices.Build(client, Enabled, new FixedTimeProvider(Start));
        var vehicle = await RdwTestServices.AddVehicleAsync(services, "HPK98B", ct);

        var info = await RdwTestServices.RefreshAsync(services, vehicle, ct);

        Assert.NotNull(info);
        Assert.True(info.Found);
        Assert.Equal("HPK98B", info.LicensePlate);
        Assert.Equal(new DateOnly(2027, 6, 1), info.ApkExpiresOn);
        Assert.Equal(Start.UtcDateTime, info.FetchedAt);
        Assert.Null(info.LastError);
        var recalls = RdwRecalls.Parse(info.RecallsJson);
        Assert.Equal(["MGP240278", "MGP260056"], recalls.Select(r => r.Code));
        Assert.All(recalls, r => Assert.Equal(Start.UtcDateTime, r.FirstSeenAt));
        Assert.Equal("risk", recalls[0].Risk);
        Assert.Equal(["MGP240278", "MGP260056"], Assert.Single(client.DetailRequests));
    }

    // The details of a recall do not change; they are only asked for one not seen before.
    [Fact]
    public async Task NextLookup_AsksDetailsOnlyForANewRecall()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FixedTimeProvider(Start);
        var client = new FakeRdwClient
        {
            Statuses = [new("MGP240278", "O")],
            Details = [new("MGP240278", "defect", "risk", "repair", "GEM"), new("MGP260068", "d", "r", "f", "ERN")],
        };
        await using var services = RdwTestServices.Build(client, Enabled, time);
        var vehicle = await RdwTestServices.AddVehicleAsync(services, "00BLS4", ct);
        await RdwTestServices.RefreshAsync(services, vehicle, ct);

        time.Advance(TimeSpan.FromDays(1));
        await RdwTestServices.RefreshAsync(services, vehicle, ct);
        client.Statuses = [new("MGP240278", "P"), new("MGP260068", "O")];
        var info = await RdwTestServices.RefreshAsync(services, vehicle, ct);

        Assert.Equal(2, client.DetailRequests.Count);
        Assert.Equal(["MGP260068"], client.DetailRequests[1]);
        var recalls = RdwRecalls.Parse(info!.RecallsJson);
        Assert.Equal(Start.UtcDateTime, recalls.Single(r => r.Code == "MGP240278").FirstSeenAt);
        Assert.Equal("P", recalls.Single(r => r.Code == "MGP240278").Status);
        Assert.Equal(time.GetUtcNow().UtcDateTime, recalls.Single(r => r.Code == "MGP260068").FirstSeenAt);
    }

    [Fact]
    public async Task UnknownPlate_IsSavedAsNotFound_WithoutAskingForRecalls()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = new FakeRdwClient { Registration = null };
        await using var services = RdwTestServices.Build(client, Enabled, new FixedTimeProvider(Start));
        var vehicle = await RdwTestServices.AddVehicleAsync(services, "ZZ999Z", ct);

        var info = await RdwTestServices.RefreshAsync(services, vehicle, ct);

        Assert.False(info!.Found);
        Assert.Null(info.LastError);
        Assert.Equal(Start.UtcDateTime, info.FetchedAt);
        Assert.Equal(0, client.StatusRequests);
    }

    [Fact]
    public async Task Failure_KeepsTheLastAnswer_AndSaysWhy()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FixedTimeProvider(Start);
        var client = new FakeRdwClient();
        await using var services = RdwTestServices.Build(client, Enabled, time);
        var vehicle = await RdwTestServices.AddVehicleAsync(services, "HPK98B", ct);
        await RdwTestServices.RefreshAsync(services, vehicle, ct);

        time.Advance(TimeSpan.FromDays(1));
        client.Failure = new RdwUnavailableException("The RDW answered 500");
        var info = await RdwTestServices.RefreshAsync(services, vehicle, ct);

        Assert.Equal("The RDW answered 500", info!.LastError);
        Assert.Equal(Start.UtcDateTime, info.FetchedAt);
        Assert.Equal(new DateOnly(2027, 6, 1), info.ApkExpiresOn);
    }

    // A push the Worker marked while a lookup was running survives that lookup's save.
    [Fact]
    public async Task Save_KeepsARecallMarkedPushedMeanwhile()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = new FakeRdwClient { Statuses = [new("MGP240278", "O")] };
        await using var services = RdwTestServices.Build(client, Enabled, new FixedTimeProvider(Start));
        var vehicle = await RdwTestServices.AddVehicleAsync(services, "HPK98B", ct);
        await RdwTestServices.RefreshAsync(services, vehicle, ct);

        await using (var scope = services.CreateAsyncScope())
        {
            var rdw = scope.ServiceProvider.GetRequiredService<GarageStack.Core.Interfaces.IRdwRepository>();
            var stale = await rdw.GetAsync(vehicle.Id, ct);
            await rdw.MarkRecallsNotifiedAsync(vehicle.Id, ["MGP240278"], Start.UtcDateTime, ct);
            await rdw.SaveAsync(stale!, ct);
        }

        var recall = Assert.Single(RdwRecalls.Parse((await RdwTestServices.InfoAsync(services, vehicle.Id, ct))!.RecallsJson));
        Assert.Equal(Start.UtcDateTime, recall.NotifiedAt);
    }

    [Fact]
    public async Task IsDue_OnceTheLookupIsOlderThanTheInterval_OrForAnotherPlate()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FixedTimeProvider(Start);
        await using var services = RdwTestServices.Build(new FakeRdwClient(), new RdwOptions { Enabled = true, RefreshHours = 24 }, time);
        var vehicle = await RdwTestServices.AddVehicleAsync(services, "HPK98B", ct);
        var info = await RdwTestServices.RefreshAsync(services, vehicle, ct);
        await using var scope = services.CreateAsyncScope();
        var refresher = scope.ServiceProvider.GetRequiredService<RdwRefresher>();

        var fresh = refresher.IsDue(vehicle, info);
        var anotherPlate = refresher.IsDue(new Vehicle { Id = vehicle.Id, LicensePlate = "AB123C" }, info);
        time.Advance(TimeSpan.FromHours(23));
        var nearly = refresher.IsDue(vehicle, info);
        time.Advance(TimeSpan.FromHours(1));
        var stale = refresher.IsDue(vehicle, info);

        Assert.False(fresh);
        Assert.True(anotherPlate);
        Assert.False(nearly);
        Assert.True(stale);
        Assert.True(refresher.IsDue(vehicle, null));
        Assert.False(refresher.IsDue(new Vehicle { Id = vehicle.Id }, null));
    }

    // Switched off, nothing is asked: not by the refresher, not by the real client behind it.
    [Fact]
    public async Task SwitchedOff_MakesNoHttpCall()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new FakeHttpHandler("[]");
        var disabled = new RdwOptions();
        var client = new RdwApiClient(new FakeHttpClientFactory(handler), disabled, NullLogger<RdwApiClient>.Instance);
        await using var services = RdwTestServices.Build(client, disabled, new FixedTimeProvider(Start));
        var vehicle = await RdwTestServices.AddVehicleAsync(services, "HPK98B", ct);

        var info = await RdwTestServices.RefreshAsync(services, vehicle, ct);
        await using var scope = services.CreateAsyncScope();

        Assert.Null(info);
        Assert.False(scope.ServiceProvider.GetRequiredService<RdwRefresher>().IsDue(vehicle, null));
        Assert.Null(await RdwTestServices.InfoAsync(services, vehicle.Id, ct));
        Assert.Equal(0, handler.CallCount);
    }
}
