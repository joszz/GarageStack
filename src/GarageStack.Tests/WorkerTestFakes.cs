using GarageStack.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GarageStack.Tests;

// Shared across MqttConsumerServiceTests and PushNotificationCheckServiceTests, which both
// construct a worker BackgroundService that needs an IServiceScopeFactory and an IPushSender.

internal sealed class FakePushSender : IPushSender
{
    public List<(string Title, string Body)> Sent { get; } = [];

    public Task SendToAllAsync(string title, string body, CancellationToken ct = default, string? category = null, int? vehicleId = null)
    {
        Sent.Add((title, body));
        return Task.CompletedTask;
    }
}

internal sealed class FakeServiceScopeFactory : IServiceScopeFactory
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
