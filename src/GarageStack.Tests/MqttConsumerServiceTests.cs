using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Worker.Mqtt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Diagnostics.PacketInspection;
using MQTTnet.Packets;

namespace GarageStack.Tests;

// ---------------------------------------------------------------------------
// Shared fakes
// ---------------------------------------------------------------------------

file sealed class FakePushSender : IPushSender
{
    public List<(string Title, string Body)> Sent { get; } = [];

    public Task SendToAllAsync(string title, string body, CancellationToken ct = default)
    {
        Sent.Add((title, body));
        return Task.CompletedTask;
    }
}

file sealed class FakeServiceScopeFactory : IServiceScopeFactory
{
    public IServiceScope CreateScope() => new FakeScope();

    private sealed class FakeScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new FakeServiceProvider();
        public void Dispose() { }
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}

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
    public async Task CheckEngineStartAsync_FirstStartTransition_SendsPushNotification()
    {
        var push = new FakePushSender();
        var svc = CreateService(push);

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
        await svc.CheckEngineStartAsync("VIN1", snap, CancellationToken.None);
        await svc.CheckEngineStartAsync("VIN1", snap, CancellationToken.None);

        Assert.Single(push.Sent);
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
    public async Task CheckEngineStartAsync_AfterRestart_SendsNotificationAgain()
    {
        var push = new FakePushSender();
        var svc = CreateService(push);

        await svc.CheckEngineStartAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, CancellationToken.None);
        await svc.CheckEngineStartAsync("VIN1", new TelemetrySnapshot { EngineRunning = false }, CancellationToken.None);
        await svc.CheckEngineStartAsync("VIN1", new TelemetrySnapshot { EngineRunning = true }, CancellationToken.None);

        Assert.Equal(2, push.Sent.Count);
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
