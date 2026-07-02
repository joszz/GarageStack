using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Worker.Mqtt;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Diagnostics.PacketInspection;
using MQTTnet.Packets;

namespace GarageStack.Tests;

// FakePushSender / FakeServiceScopeFactory live in WorkerTestFakes.cs (shared with
// PushNotificationCheckServiceTests).

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

    // Call from a test to simulate an incoming message on the subscribed topics.
    public Task TriggerMessageAsync(string topic, string payload)
    {
        if (_msgHandler is null) return Task.CompletedTask;

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
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

    public TestableMqttConsumerService(FakePushSender push, FakeMqttClient client)
        : base(NullLogger<MqttConsumerService>.Instance, Options.Create(new MqttOptions()), new FakeServiceScopeFactory(), push)
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
// Engine-start notification tests (unchanged logic)
// ---------------------------------------------------------------------------

public class MqttConsumerServiceTests
{
    private static MqttConsumerService CreateService(IPushSender push) =>
        new(
            NullLogger<MqttConsumerService>.Instance,
            Options.Create(new MqttOptions()),
            new FakeServiceScopeFactory(),
            push);

    [Fact]
    public async Task CheckEngineStartAsync_FirstObservationRunning_SeedsWithoutFiring()
    {
        var push = new FakePushSender();
        var svc = CreateService(push);

        // On restart the car may already be running; the first observation must not fire.
        await svc.CheckEngineStartAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, CancellationToken.None);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task CheckEngineStartAsync_GenuineTransition_SendsPushNotification()
    {
        var push = new FakePushSender();
        var svc = CreateService(push);

        // Seed the state with the engine off, then observe a start.
        await svc.CheckEngineStartAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, CancellationToken.None);
        await svc.CheckEngineStartAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, CancellationToken.None);

        Assert.Single(push.Sent);
        Assert.Equal("Engine started", push.Sent[0].Title);
    }

    [Fact]
    public async Task CheckEngineStartAsync_AlreadyRunning_DoesNotSendAgain()
    {
        var push = new FakePushSender();
        var svc = CreateService(push);

        var snap = new TelemetrySnapshot { EngineRunning = true };
        // First: seed. Second: no transition. Neither fires.
        await svc.CheckEngineStartAsync("VIN1", snap, CancellationToken.None);
        await svc.CheckEngineStartAsync("VIN1", snap, CancellationToken.None);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task CheckEngineStartAsync_EngineOff_DoesNotSendNotification()
    {
        var push = new FakePushSender();
        var svc = CreateService(push);

        await svc.CheckEngineStartAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, CancellationToken.None);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task CheckEngineStartAsync_StartStopStart_SendsOneNotification()
    {
        var push = new FakePushSender();
        var svc = CreateService(push);

        // Seed (no fire), stop, start -> exactly one notification.
        await svc.CheckEngineStartAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, CancellationToken.None);
        await svc.CheckEngineStartAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, CancellationToken.None);
        await svc.CheckEngineStartAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, CancellationToken.None);

        Assert.Single(push.Sent);
    }

    [Fact]
    public async Task CheckEngineStartAsync_NullEngineRunning_SkipsCheck()
    {
        var push = new FakePushSender();
        var svc = CreateService(push);

        await svc.CheckEngineStartAsync("VIN1", new TelemetrySnapshot { EngineRunning = null }, CancellationToken.None);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task CheckEngineStartAsync_MultipleVins_TracksStateIndependently()
    {
        var push = new FakePushSender();
        var svc = CreateService(push);

        // Seed both VINs as off, then start each one independently.
        await svc.CheckEngineStartAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, CancellationToken.None);
        await svc.CheckEngineStartAsync("VIN2", new TelemetrySnapshot { EngineRunning = false }, CancellationToken.None);
        await svc.CheckEngineStartAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, CancellationToken.None);
        await svc.CheckEngineStartAsync("VIN2", new TelemetrySnapshot { EngineRunning = true }, CancellationToken.None);

        Assert.Equal(2, push.Sent.Count);
    }
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
// pipeline, matching HandleHaDiscoveryAsync's early-return / catch-and-log design.
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
        // HandleHaDiscoveryAsync will fail - but it's caught and logged, not thrown.
        await client.TriggerMessageAsync("homeassistant/sensor/garagestack/config", payload);

        await cts.CancelAsync();
        try { await serviceTask; } catch (OperationCanceledException) { }
    }
}
