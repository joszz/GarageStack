using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using GarageStack.Data.Services;
using GarageStack.Worker;
using GarageStack.Worker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

// The Worker's round over the real repositories: when it looks a plate up, and which pushes come
// of what the RDW said.
public class RdwSyncServiceTests
{
    private static readonly IStringLocalizer<NotificationStrings> Strings = WorkerLocalizer.Notifications();
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private static readonly RdwOptions Enabled = new() { Enabled = true };

    private sealed class OnePass(IServiceScopeFactory scopes, FakePushSender push, RdwOptions options, TimeProvider time)
        : RdwSyncService(NullLogger<RdwSyncService>.Instance, scopes, push, options, Strings, time)
    {
        public Task RunAsync(CancellationToken ct) => RunOnceAsync(ct);

        public Task RunServiceAsync(CancellationToken ct) => ExecuteAsync(ct);
    }

    private static OnePass Service(ServiceProvider services, FakePushSender push, RdwOptions options, TimeProvider time) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), push, options, time);

    [Fact]
    public async Task NewOpenRecall_IsPushedOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FixedTimeProvider(Start);
        var client = new FakeRdwClient
        {
            Statuses = [new("MGP230061", "O")],
            Details = [new("MGP230061", "De aardingskabel zit los.", "Brand", "Vastzetten", "ERN")],
        };
        await using var services = RdwTestServices.Build(client, Enabled, time);
        await RdwTestServices.AddVehicleAsync(services, "HPK98B", ct);
        var push = new FakePushSender();
        var service = Service(services, push, Enabled, time);

        await service.RunAsync(ct);
        time.Advance(TimeSpan.FromHours(1));
        await service.RunAsync(ct);

        var sent = Assert.Single(push.Sent);
        Assert.Equal("rdw-recall-MGP230061", sent.Category);
        Assert.Equal("Open recall", sent.Title);
        Assert.Contains("MGP230061", sent.Body);
        Assert.Contains("De aardingskabel zit los.", sent.Body);
    }

    // Repaired is no news, and a recall the API's lookup found is pushed by the Worker's next round.
    [Fact]
    public async Task RecallRepaired_IsNotPushedAgain_ButASecondRecallIs()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FixedTimeProvider(Start);
        var client = new FakeRdwClient { Statuses = [new("MGP240278", "O")] };
        await using var services = RdwTestServices.Build(client, new RdwOptions { Enabled = true, RefreshHours = 24 }, time);
        var vehicle = await RdwTestServices.AddVehicleAsync(services, "00BLS4", ct);
        var push = new FakePushSender();
        var service = Service(services, push, Enabled, time);

        // The API looks the plate up the moment it is entered; the Worker pushes what it found.
        await RdwTestServices.RefreshAsync(services, vehicle, ct);
        await service.RunAsync(ct);

        time.Advance(TimeSpan.FromHours(25));
        client.Statuses = [new("MGP240278", "P"), new("MGP260068", "O")];
        await service.RunAsync(ct);
        time.Advance(TimeSpan.FromHours(25));
        await service.RunAsync(ct);

        Assert.Equal(["rdw-recall-MGP240278", "rdw-recall-MGP260068"], push.Sent.Select(s => s.Category));
        Assert.Equal(3, client.StatusRequests);
        var recalls = RdwRecalls.Parse((await RdwTestServices.InfoAsync(services, vehicle.Id, ct))!.RecallsJson);
        Assert.All(recalls, r => Assert.NotNull(r.NotifiedAt));
    }

    // A recall first seen already repaired was never something to act on.
    [Fact]
    public async Task RecallFirstSeenRepaired_IsNotPushed()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FixedTimeProvider(Start);
        var client = new FakeRdwClient { Statuses = [new("MGP200235", "P")] };
        await using var services = RdwTestServices.Build(client, Enabled, time);
        await RdwTestServices.AddVehicleAsync(services, "00BPJ3", ct);
        var push = new FakePushSender();

        await Service(services, push, Enabled, time).RunAsync(ct);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task Apk_IsPushedAMonthAndAWeekAhead_AndOnceItRanOut()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FixedTimeProvider(Start);
        // 40 days from 19 October.
        var client = new FakeRdwClient { Registration = new("MG", "MG4", new DateOnly(2026, 11, 28), true, false) };
        await using var services = RdwTestServices.Build(client, Enabled, time);
        await RdwTestServices.AddVehicleAsync(services, "AB123C", ct);
        var push = new FakePushSender();
        var service = Service(services, push, Enabled, time);

        // A pass a day for seven weeks.
        for (var day = 0; day < 49; day++)
        {
            await service.RunAsync(ct);
            time.Advance(TimeSpan.FromDays(1));
        }

        Assert.Equal(
            ["rdw-apk-due-30-20261128", "rdw-apk-due-7-20261128", "rdw-apk-expired-20261128"],
            push.Sent.Select(s => s.Category));
        Assert.Equal("The APK runs out on 28 November 2026, in 30 days.", push.Sent[0].Body);
        Assert.Equal("The APK runs out on 28 November 2026, in 7 days.", push.Sent[1].Body);
        Assert.Equal("APK expired", push.Sent[2].Title);
    }

    // A renewed APK has a new date, and so its own reminders.
    [Fact]
    public async Task Apk_Renewed_GetsItsOwnReminders()
    {
        var due = new RdwVehicleInfo { Found = true, ApkExpiresOn = new DateOnly(2026, 11, 8) };
        var renewed = new RdwVehicleInfo { Found = true, ApkExpiresOn = new DateOnly(2027, 11, 8) };

        var first = RdwSyncService.BuildAlerts(due, Start.UtcDateTime, Strings).Single();
        var nextYear = RdwSyncService.BuildAlerts(renewed, new DateTime(2027, 10, 19, 8, 0, 0, DateTimeKind.Utc), Strings).Single();

        Assert.Equal("rdw-apk-due-30-20261108", first.Category);
        Assert.Equal("rdw-apk-due-30-20271108", nextYear.Category);
    }

    [Theory]
    [InlineData(0, "Today, 19 October 2026, is the APK's last day.")]
    [InlineData(1, "The APK runs out tomorrow, 20 October 2026.")]
    [InlineData(3, "The APK runs out on 22 October 2026, in 3 days.")]
    public void ApkDueBody_SaysWhenInWords(int daysLeft, string expected)
    {
        var info = new RdwVehicleInfo { Found = true, ApkExpiresOn = new DateOnly(2026, 10, 19).AddDays(daysLeft) };

        Assert.Equal(expected, RdwSyncService.BuildAlerts(info, Start.UtcDateTime, Strings).Single().Body);
    }

    [Fact]
    public void NotInsured_IsAnAlert_AndUnknownIsNot()
    {
        var uninsured = new RdwVehicleInfo { Found = true, WamInsured = false };
        var unknown = new RdwVehicleInfo { Found = true, WamInsured = null };

        Assert.Equal(NotificationCategories.RdwUninsured, RdwSyncService.BuildAlerts(uninsured, Start.UtcDateTime, Strings).Single().Category);
        Assert.Empty(RdwSyncService.BuildAlerts(unknown, Start.UtcDateTime, Strings));
    }

    [Fact]
    public async Task NotInsured_IsPushed_AndHeldBackOnTheNextRound()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FixedTimeProvider(Start);
        var client = new FakeRdwClient { Registration = new("MAN", "TGS", null, false, false) };
        await using var services = RdwTestServices.Build(client, Enabled, time);
        await RdwTestServices.AddVehicleAsync(services, "00BLS4", ct);
        var push = new FakePushSender();
        var service = Service(services, push, Enabled, time);

        await service.RunAsync(ct);
        time.Advance(TimeSpan.FromHours(1));
        await service.RunAsync(ct);

        Assert.Equal(["rdw-uninsured"], push.Sent.Select(s => s.Category));
        Assert.Equal("No insurance on record", push.Sent[0].Title);
    }

    // An APK renewed while the RDW could not be reached must not be reported as running out.
    [Fact]
    public async Task OldLookup_IsNotPushedFrom()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FixedTimeProvider(Start);
        var client = new FakeRdwClient { Registration = new("MG", "MG4", new DateOnly(2026, 11, 28), true, false) };
        await using var services = RdwTestServices.Build(client, Enabled, time);
        var vehicle = await RdwTestServices.AddVehicleAsync(services, "AB123C", ct);
        await RdwTestServices.RefreshAsync(services, vehicle, ct);
        var push = new FakePushSender();

        time.Advance(TimeSpan.FromDays(20));
        client.Failure = new RdwUnavailableException("The RDW could not be reached");
        await Service(services, push, Enabled, time).RunAsync(ct);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task UnknownPlate_IsNotPushedAbout()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FixedTimeProvider(Start);
        await using var services = RdwTestServices.Build(new FakeRdwClient { Registration = null }, Enabled, time);
        await RdwTestServices.AddVehicleAsync(services, "ZZ999Z", ct);
        var push = new FakePushSender();

        await Service(services, push, Enabled, time).RunAsync(ct);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task VehicleWithoutAPlate_IsNotLookedUp()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FixedTimeProvider(Start);
        var client = new FakeRdwClient();
        await using var services = RdwTestServices.Build(client, Enabled, time);
        await RdwTestServices.AddVehicleAsync(services, null, ct);

        await Service(services, new FakePushSender(), Enabled, time).RunAsync(ct);

        Assert.Equal(0, client.RegistrationRequests);
    }

    // Off, the job stops before its first pass: no database, no RDW, no push.
    [Fact]
    public async Task SwitchedOff_StopsAtOnce_AndMakesNoHttpCall()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new FakeHttpHandler("[]");
        var disabled = new RdwOptions();
        var client = new RdwApiClient(new FakeHttpClientFactory(handler), disabled, NullLogger<RdwApiClient>.Instance);
        var time = new FixedTimeProvider(Start);
        await using var services = RdwTestServices.Build(client, disabled, time);
        await RdwTestServices.AddVehicleAsync(services, "HPK98B", ct);
        var push = new FakePushSender();
        var scopes = new FakeServiceScopeFactory();

        var stopped = new OnePass(scopes, push, disabled, time).RunServiceAsync(ct);
        await stopped.WaitAsync(TimeSpan.FromSeconds(5), ct);
        // Even a pass forced through finds nothing to do.
        await Service(services, push, disabled, time).RunAsync(ct);

        Assert.True(stopped.IsCompletedSuccessfully);
        Assert.Equal(0, scopes.CreatedScopes);
        Assert.Equal(0, handler.CallCount);
        Assert.Empty(push.Sent);
    }
}
