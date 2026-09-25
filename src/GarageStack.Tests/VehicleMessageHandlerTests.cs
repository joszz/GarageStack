using System.Text.Json;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using GarageStack.Worker.Mqtt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

file sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public class VehicleMessageJudgeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewMessage_IsPushed()
    {
        Assert.Equal(VehicleMessageVerdict.Push,
            VehicleMessageHandler.Judge("301", "M2", Now.AddMinutes(-2), "M1", Now));
    }

    [Fact]
    public void MessageAlreadyDealtWith_IsARepeat()
    {
        Assert.Equal(VehicleMessageVerdict.Repeat,
            VehicleMessageHandler.Judge("301", "M1", Now.AddMinutes(-2), "M1", Now));
    }

    [Fact]
    public void VehicleStartMessage_IsLeftToTheEngineStartAlert()
    {
        Assert.Equal(VehicleMessageVerdict.VehicleStart,
            VehicleMessageHandler.Judge(VehicleMessageHandler.VehicleStartType, "M2", Now, "M1", Now));
    }

    [Fact]
    public void MessageOlderThanMaxAge_IsTooOld()
    {
        Assert.Equal(VehicleMessageVerdict.TooOld,
            VehicleMessageHandler.Judge("301", "M2", Now - VehicleMessageHandler.MaxAge - TimeSpan.FromMinutes(1), null, Now));
    }

    // SAIC's zone-less timestamps, read as UTC, can put a message ahead of the clock.
    [Fact]
    public void MessageTimedInTheFuture_IsPushed()
    {
        Assert.Equal(VehicleMessageVerdict.Push,
            VehicleMessageHandler.Judge("301", "M2", Now.AddHours(10), "M1", Now));
    }

    [Fact]
    public void UnknownTimeAndId_IsPushed()
    {
        Assert.Equal(VehicleMessageVerdict.Push,
            VehicleMessageHandler.Judge("301", null, null, "M1", Now));
    }
}

public sealed class VehicleMessageHandlerTests : IDisposable
{
    private const string Vin = "FAKEVN00000000001";
    private const string User = "user";
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;
    private readonly FakePushSender _push = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public VehicleMessageHandlerTests()
    {
        var dbName = Guid.NewGuid().ToString();
        _services = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddScoped<IVehicleRepository, VehicleRepository>()
            .BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    // A fresh handler is a fresh Worker process: its in-memory tracking starts empty, the database does not.
    private VehicleMessageHandler NewHandler() => new(
        NullLogger.Instance,
        _services.GetRequiredService<IServiceScopeFactory>(),
        _push,
        WorkerLocalizer.Notifications(),
        new FixedTimeProvider(Now));

    // What the gateway publishes for one message, in its order: time, id, then the event.
    private async Task PublishAsync(
        VehicleMessageHandler handler, string id, DateTimeOffset sentAt,
        string title = "Vehicle alarm", string content = "The alarm went off.", string messageType = "301")
    {
        var evt = JsonSerializer.Serialize(new { event_type = "vehicle_message", title, content, message_type = messageType, sender = "iSMART", vin = Vin });
        await handler.TryHandleAsync(Vin, User, GatewayVehicleMessage.SentAtSubtopic, sentAt.ToString("o"), retained: false, _ct);
        await handler.TryHandleAsync(Vin, User, GatewayVehicleMessage.IdSubtopic, id, retained: false, _ct);
        await handler.TryHandleAsync(Vin, User, GatewayVehicleMessage.EventSubtopic, evt, retained: false, _ct);
    }

    private string? StoredLastMessageId()
    {
        using var scope = _services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Vehicles.Single().LastMessageId;
    }

    [Fact]
    public async Task NewMessage_IsPushedAndRemembered()
    {
        await PublishAsync(NewHandler(), "M1", Now.AddMinutes(-1));

        var sent = Assert.Single(_push.Sent);
        Assert.Equal(("Vehicle alarm", "The alarm went off.", NotificationCategories.VehicleMessage), sent);
        Assert.Equal("M1", StoredLastMessageId());
    }

    [Fact]
    public async Task GatewayRestart_RepeatOfTheLatestMessage_IsNotPushedAgain()
    {
        var handler = NewHandler();
        await PublishAsync(handler, "M1", Now.AddMinutes(-1));

        await PublishAsync(handler, "M1", Now.AddMinutes(-1));

        Assert.Single(_push.Sent);
    }

    // The all-in-one image restarts the Worker together with the gateway, so the repeat meets a
    // Worker that remembers nothing but what the database holds.
    [Fact]
    public async Task WorkerAndGatewayRestart_RepeatOfTheLatestMessage_IsNotPushedAgain()
    {
        await PublishAsync(NewHandler(), "M1", Now.AddMinutes(-1));

        await PublishAsync(NewHandler(), "M1", Now.AddMinutes(-1));

        Assert.Single(_push.Sent);
    }

    [Fact]
    public async Task FreshInstall_AccountsOldLastMessage_IsRememberedButNotPushed()
    {
        await PublishAsync(NewHandler(), "M1", Now.AddDays(-40));

        Assert.Empty(_push.Sent);
        Assert.Equal("M1", StoredLastMessageId());
    }

    // An id the broker replays on subscribe is a message whose event came while the Worker was
    // away: when the gateway repeats it later, it is late, not new.
    [Fact]
    public async Task RetainedIdReplay_IsRemembered_SoTheGatewaysRepeatIsNotPushed()
    {
        var handler = NewHandler();
        await handler.TryHandleAsync(Vin, User, GatewayVehicleMessage.IdSubtopic, "M1", retained: true, _ct);
        Assert.Equal("M1", StoredLastMessageId());

        await PublishAsync(handler, "M1", Now.AddMinutes(-5));

        Assert.Empty(_push.Sent);
    }

    [Fact]
    public async Task VehicleStartMessage_IsRememberedButNotPushed()
    {
        await PublishAsync(NewHandler(), "M1", Now, messageType: VehicleMessageHandler.VehicleStartType);

        Assert.Empty(_push.Sent);
        Assert.Equal("M1", StoredLastMessageId());
    }

    [Fact]
    public async Task NextMessage_AfterOneAlreadyDealtWith_IsPushed()
    {
        var handler = NewHandler();
        await PublishAsync(handler, "M1", Now.AddHours(-2));

        await PublishAsync(handler, "M2", Now.AddMinutes(-1), title: "Service reminder", content: "Your car is due for a service.");

        Assert.Equal(2, _push.Sent.Count);
        Assert.Equal("Service reminder", _push.Sent[1].Title);
        Assert.Equal("M2", StoredLastMessageId());
    }

    [Fact]
    public async Task MessageWithoutTitle_GetsTheLocalizedFallback()
    {
        await PublishAsync(NewHandler(), "M1", Now, title: "");

        Assert.Equal("Message from MG", Assert.Single(_push.Sent).Title);
    }

    [Fact]
    public async Task RetainedEvent_IsNotPushed()
    {
        var handler = NewHandler();
        await handler.TryHandleAsync(Vin, User, GatewayVehicleMessage.IdSubtopic, "M1", retained: false, _ct);

        var handled = await handler.TryHandleAsync(
            Vin, User, GatewayVehicleMessage.EventSubtopic, """{"title":"Vehicle alarm","content":"x"}""", retained: true, _ct);

        Assert.True(handled);
        Assert.Empty(_push.Sent);
    }

    [Fact]
    public async Task UnreadableEvent_IsHandledWithoutPushing()
    {
        var handled = await NewHandler().TryHandleAsync(Vin, User, GatewayVehicleMessage.EventSubtopic, "not json", retained: false, _ct);

        Assert.True(handled);
        Assert.Empty(_push.Sent);
    }

    [Theory]
    [InlineData("info/lastMessage/title")]
    [InlineData("drivetrain/soc")]
    [InlineData("doors/locked/result")]
    public async Task OtherSubtopics_AreLeftToTheCaller(string subtopic)
    {
        Assert.False(await NewHandler().TryHandleAsync(Vin, User, subtopic, "x", retained: false, _ct));
    }
}
