using GarageStack.Core.Configuration;
using GarageStack.Worker.Mqtt;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Diagnostics.PacketInspection;
using MQTTnet.Packets;

namespace GarageStack.Tests;

// Drives the consumer service itself: its reconnect loop, and which handler each message
// reaches. The handlers' own decisions are tested in their own classes (TelemetryHandlerTests,
// VehicleMessageHandlerTests). FakePushSender / FakeServiceScopeFactory live in WorkerTestFakes.cs.

// ---------------------------------------------------------------------------
// FakeMqttClient -- controllable IMqttClient for reconnect-loop tests
// ---------------------------------------------------------------------------

file sealed class FakeMqttClient : IMqttClient
{
    private readonly Queue<Func<CancellationToken, Task>> _connectBehaviors = new();
    private readonly SemaphoreSlim _connectCalled = new(0);
    private Func<MqttApplicationMessageReceivedEventArgs, Task>? _msgHandler;
    private Func<MqttClientDisconnectedEventArgs, Task>? _disconnectedHandler;

    public int ConnectCount { get; private set; }
    public bool IsConnected { get; private set; }
    public MqttClientOptions Options { get; } = new();

    public event Func<MqttApplicationMessageReceivedEventArgs, Task>? ApplicationMessageReceivedAsync
    {
        add => _msgHandler += value;
        remove => _msgHandler -= value;
    }

    public event Func<MqttClientConnectedEventArgs, Task>? ConnectedAsync { add { } remove { } }
    public event Func<MqttClientConnectingEventArgs, Task>? ConnectingAsync { add { } remove { } }

    public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync
    {
        add => _disconnectedHandler += value;
        remove => _disconnectedHandler -= value;
    }

    public event Func<InspectMqttPacketEventArgs, Task>? InspectPacketAsync { add { } remove { } }

    // Queue a one-shot behavior for the next ConnectAsync call.
    // If nothing is queued, ConnectAsync returns success immediately.
    public void QueueConnectBehavior(Func<CancellationToken, Task> behavior) =>
        _connectBehaviors.Enqueue(behavior);

    public async Task<MqttClientConnectResult> ConnectAsync(MqttClientOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ConnectCount++;
        _connectCalled.Release();

        if (_connectBehaviors.TryDequeue(out var behavior))
            await behavior(ct); // may throw to simulate connection error

        IsConnected = true;
        return new MqttClientConnectResult();
    }

    public Task<MqttClientSubscribeResult> SubscribeAsync(MqttClientSubscribeOptions options, CancellationToken ct) =>
        Task.FromResult<MqttClientSubscribeResult>(null!);

    public Task DisconnectAsync(MqttClientDisconnectOptions? options = null, CancellationToken ct = default)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    // Call from a test to simulate the broker closing the connection.
    public async Task TriggerDisconnectAsync()
    {
        IsConnected = false;
        if (_disconnectedHandler is not null)
        {
            var args = new MqttClientDisconnectedEventArgs(
                true,
                new MqttClientConnectResult(),
                MqttClientDisconnectReason.NormalDisconnection,
                string.Empty,
                new List<MqttUserProperty>(),
                null!);
            await _disconnectedHandler(args);
        }
    }

    // Waits until ConnectAsync is called one more time (with a generous timeout).
    public Task WaitForConnectAsync(TimeSpan? timeout = null) =>
        _connectCalled.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));

    // Call from a test to simulate an incoming message on the subscribed topics. retain marks it as
    // the broker's replay of a retained message rather than a live publish.
    public Task TriggerMessageAsync(string topic, string payload, bool retain = false)
    {
        if (_msgHandler is null) return Task.CompletedTask;

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithRetainFlag(retain)
            .Build();
        var args = new MqttApplicationMessageReceivedEventArgs(
            "test-client", message, new MqttPublishPacket(), (_, _) => Task.CompletedTask);
        return _msgHandler(args);
    }

    public Task PingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<MqttClientPublishResult> PublishAsync(MqttApplicationMessage msg, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task SendEnhancedAuthenticationExchangeDataAsync(MqttEnhancedAuthenticationExchangeData data, CancellationToken ct) =>
        throw new NotImplementedException();
    public Task<MqttClientUnsubscribeResult> UnsubscribeAsync(MqttClientUnsubscribeOptions options, CancellationToken ct) =>
        throw new NotImplementedException();

    public void Dispose() { }
}

// ---------------------------------------------------------------------------
// Testable subclass -- overrides the two virtual hooks
// ---------------------------------------------------------------------------

file sealed class TestableMqttConsumerService : MqttConsumerService
{
    private readonly FakeMqttClient _client;

    public TestableMqttConsumerService(FakePushSender push, FakeMqttClient client, FakeServiceScopeFactory? scopes = null)
        : base(NullLogger<MqttConsumerService>.Instance, Options.Create(new MqttOptions()), scopes ?? new FakeServiceScopeFactory(), push, WorkerLocalizer.Notifications(), new CommandAnswerWaiter())
    {
        _client = client;
    }

    protected override IMqttClient CreateMqttClient() => _client;

    // Zero-delay retries so reconnect tests complete without sleeping.
    protected override TimeSpan RetryDelay => TimeSpan.Zero;

    // Expose ExecuteAsync publicly so tests can drive it directly.
    public Task RunAsync(CancellationToken ct) => ExecuteAsync(ct);
}

// ---------------------------------------------------------------------------
// Reconnect-loop tests -- simulate broker disconnect/reconnect and error paths
// ---------------------------------------------------------------------------

public class MqttConsumerServiceReconnectTests
{
    [Fact]
    public async Task ExecuteAsync_BrokerDisconnect_Reconnects()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = new FakeMqttClient();
        var svc = new TestableMqttConsumerService(new FakePushSender(), client);

        var serviceTask = svc.RunAsync(cts.Token);

        // Wait for the service to connect and subscribe the first time.
        await client.WaitForConnectAsync();

        // Simulate the broker dropping the connection.
        await client.TriggerDisconnectAsync();

        // The service should reconnect with no delay (RetryDelay = Zero).
        await client.WaitForConnectAsync();

        await cts.CancelAsync();
        try { await serviceTask; } catch (OperationCanceledException) { }

        Assert.True(client.ConnectCount >= 2, $"Expected >= 2 connects, got {client.ConnectCount}");
    }

    [Fact]
    public async Task ExecuteAsync_ConnectException_RetriesAndSucceeds()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = new FakeMqttClient();

        // First attempt: fail with a connection error.
        client.QueueConnectBehavior(_ => Task.FromException(new System.Net.Sockets.SocketException()));

        var svc = new TestableMqttConsumerService(new FakePushSender(), client);
        var serviceTask = svc.RunAsync(cts.Token);

        // Wait for the failed first attempt and the successful retry.
        await client.WaitForConnectAsync(); // attempt 1 (exception)
        await client.WaitForConnectAsync(); // attempt 2 (success)

        await cts.CancelAsync();
        try { await serviceTask; } catch (OperationCanceledException) { }

        Assert.True(client.ConnectCount >= 2, $"Expected >= 2 connects, got {client.ConnectCount}");
    }

    [Fact]
    public async Task ExecuteAsync_CancellationWhileConnected_ExitsWithoutHanging()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = new FakeMqttClient();
        var svc = new TestableMqttConsumerService(new FakePushSender(), client);

        var serviceTask = svc.RunAsync(cts.Token);

        // Wait until connected, then cancel.
        await client.WaitForConnectAsync();
        await cts.CancelAsync();

        // Service must complete without hanging.
        var completed = await Task.WhenAny(serviceTask, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Same(serviceTask, completed);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleDisconnects_ReconnectsEachTime()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = new FakeMqttClient();
        var svc = new TestableMqttConsumerService(new FakePushSender(), client);

        var serviceTask = svc.RunAsync(cts.Token);

        for (var i = 0; i < 3; i++)
        {
            await client.WaitForConnectAsync();
            await client.TriggerDisconnectAsync();
        }

        await cts.CancelAsync();
        try { await serviceTask; } catch (OperationCanceledException) { }

        Assert.True(client.ConnectCount >= 3, $"Expected >= 3 connects, got {client.ConnectCount}");
    }
}

// ---------------------------------------------------------------------------
// HA discovery payload parsing -- these all resolve before (or independently of)
// the DI scope, so they're reachable with the null-service FakeServiceScopeFactory.
// The assertion is that malformed/incomplete payloads never crash the message
// pipeline, matching HaDiscoveryHandler's early-return / catch-and-log design.
// ---------------------------------------------------------------------------

public class MqttConsumerServiceHaDiscoveryTests
{
    [Theory]
    [InlineData("""{"device":{"identifiers":["FAKEVN00000000001"]}}""")] // missing hw_version
    [InlineData("""{"device":{"hw_version":"MG_BEV_1.0"}}""")] // missing identifiers
    [InlineData("not json but mentions hw_version and identifiers")] // not valid JSON at all
    [InlineData("""{"foo":{"hw_version":"x","identifiers":["VIN"]}}""")] // missing "device" property
    [InlineData("""{"device":{"identifiers":["VIN"],"other":"hw_version"}}""")] // device has no hw_version property
    [InlineData("""{"device":{"hw_version":"MG_BEV_1.0","identifiers":[]}}""")] // empty identifiers array
    [InlineData("""{"device":{"hw_version":"MG_BEV_1.0","identifiers":[123,456]}}""")] // non-string identifiers
    public async Task HandleHaDiscovery_IncompleteOrMalformedPayload_DoesNotThrow(string payload)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = new FakeMqttClient();
        var svc = new TestableMqttConsumerService(new FakePushSender(), client);
        var serviceTask = svc.RunAsync(cts.Token);
        await client.WaitForConnectAsync();

        // Should return quietly (or be caught internally) rather than propagate.
        await client.TriggerMessageAsync("homeassistant/sensor/garagestack/config", payload);

        await cts.CancelAsync();
        try { await serviceTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task HandleHaDiscovery_ValidPayload_DoesNotThrowEvenWhenScopeResolutionFails()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = new FakeMqttClient();
        var svc = new TestableMqttConsumerService(new FakePushSender(), client);
        var serviceTask = svc.RunAsync(cts.Token);
        await client.WaitForConnectAsync();
        const string payload = """{"device":{"hw_version":"MG_BEV_1.0","identifiers":["FAKEVN00000000001"],"model":"MG4"}}""";

        // FakeServiceScopeFactory resolves no real services, so the DB write inside
        // HaDiscoveryHandler will fail - but it's caught and logged, not thrown.
        await client.TriggerMessageAsync("homeassistant/sensor/garagestack/config", payload);

        await cts.CancelAsync();
        try { await serviceTask; } catch (OperationCanceledException) { }
    }
}

// ---------------------------------------------------------------------------
// Every database step starts by resolving the vehicle in a new scope, so the fake scope
// factory's count tells a message acted on from a skipped one. The fake resolves no services,
// so the step then fails, which the service catches and logs.
// ---------------------------------------------------------------------------

file static class MqttConsumerHarness
{
    public static async Task<int> ScopesCreatedByAsync(string topic, string payload, bool retain)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = new FakeMqttClient();
        var scopes = new FakeServiceScopeFactory();
        var svc = new TestableMqttConsumerService(new FakePushSender(), client, scopes);
        var serviceTask = svc.RunAsync(cts.Token);
        await client.WaitForConnectAsync();

        await client.TriggerMessageAsync(topic, payload, retain);

        await cts.CancelAsync();
        try { await serviceTask; } catch (OperationCanceledException) { }
        return scopes.CreatedScopes;
    }
}

public class MqttConsumerServiceCommandResultTests
{
    private const string LockResultTopic = "saic/user/vehicles/FAKEVN00000000001/doors/locked/result";

    private static Task<int> ScopesCreatedByAsync(string topic, string payload, bool retain) =>
        MqttConsumerHarness.ScopesCreatedByAsync(topic, payload, retain);

    [Theory]
    [InlineData("Success")]
    [InlineData("Failed: vehicle is not online")]
    public async Task LiveResult_IsForwarded(string payload)
    {
        Assert.Equal(1, await ScopesCreatedByAsync(LockResultTopic, payload, retain: false));
    }

    // The broker replays the last retained result per command on every subscribe: forwarding it
    // would show a browser an old failure as if it had just happened.
    [Fact]
    public async Task RetainedResult_IsNotForwarded()
    {
        Assert.Equal(0, await ScopesCreatedByAsync(LockResultTopic, "Failed: vehicle is not online", retain: true));
    }

    // The gateway repeats every failure as a command/error event; the /result topic already carried it.
    [Fact]
    public async Task CommandErrorEvent_IsNotForwarded()
    {
        const string payload = """{"event_type":"command_error","command":"doors/locked/set","detail":"vehicle is not online"}""";

        Assert.Equal(0, await ScopesCreatedByAsync(
            "saic/user/vehicles/FAKEVN00000000001/command/error", payload, retain: false));
    }
}

// MG app messages reach VehicleMessageHandler; its own decisions are in VehicleMessageHandlerTests.
public class MqttConsumerServiceVehicleMessageTests
{
    private const string VehiclePrefix = "saic/user/vehicles/FAKEVN00000000001/";
    private const string EventPayload = """{"event_type":"vehicle_message","title":"Vehicle alarm","content":"x","message_type":"301"}""";

    [Fact]
    public async Task LiveEvent_IsHandled()
    {
        Assert.Equal(1, await MqttConsumerHarness.ScopesCreatedByAsync(
            VehiclePrefix + GatewayVehicleMessage.EventSubtopic, EventPayload, retain: false));
    }

    [Fact]
    public async Task RetainedEvent_IsNotHandled()
    {
        Assert.Equal(0, await MqttConsumerHarness.ScopesCreatedByAsync(
            VehiclePrefix + GatewayVehicleMessage.EventSubtopic, EventPayload, retain: true));
    }

    // Only a retained id is recorded; a live one waits for its event.
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task MessageId_IsRecordedOnlyWhenRetained(bool retain, int expectedScopes)
    {
        Assert.Equal(expectedScopes, await MqttConsumerHarness.ScopesCreatedByAsync(
            VehiclePrefix + GatewayVehicleMessage.IdSubtopic, "1234567890123456789", retain));
    }

    [Fact]
    public async Task MessageTime_IsOnlyTracked()
    {
        Assert.Equal(0, await MqttConsumerHarness.ScopesCreatedByAsync(
            VehiclePrefix + GatewayVehicleMessage.SentAtSubtopic, "2026-09-25T10:00:00+00:00", retain: false));
    }
}
