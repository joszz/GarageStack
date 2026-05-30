using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Worker.Mqtt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarageStack.Tests;

public class MqttConsumerServiceTests
{
    private sealed class FakePushSender : IPushSender
    {
        public List<(string Title, string Body)> Sent { get; } = [];

        public Task SendToAllAsync(string title, string body, CancellationToken ct = default)
        {
            Sent.Add((title, body));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeServiceScopeFactory : IServiceScopeFactory
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

    private static MqttConsumerService CreateService(FakePushSender push) =>
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
