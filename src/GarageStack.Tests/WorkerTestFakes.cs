using GarageStack.Core.Interfaces;
using GarageStack.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace GarageStack.Tests;

// Shared across MqttConsumerServiceTests and PushNotificationCheckServiceTests, which both
// construct a worker BackgroundService that needs an IServiceScopeFactory and an IPushSender.

internal sealed class FakePushSender : IPushSender
{
    public List<(string Title, string Body, string? Category)> Sent { get; } = [];

    public Task SendToAllAsync(string title, string body, CancellationToken ct = default, string? category = null, int? vehicleId = null)
    {
        Sent.Add((title, body, category));
        return Task.CompletedTask;
    }
}

internal sealed class FakeServiceScopeFactory : IServiceScopeFactory
{
    // Every database step starts with a scope, so a count of zero means nothing was attempted.
    private int _createdScopes;
    public int CreatedScopes => Volatile.Read(ref _createdScopes);

    public IServiceScope CreateScope()
    {
        Interlocked.Increment(ref _createdScopes);
        return new FakeScope();
    }

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

// The real resource-backed localizer, exactly as the Worker host builds it, so the tests also
// prove the resx files are found (a marker type in the wrong namespace fails silently with
// keys instead of texts).
internal static class WorkerLocalizer
{
    public static IStringLocalizer<NotificationStrings> Notifications() =>
        new ServiceCollection()
            .AddLogging()
            .AddLocalization(o => o.ResourcesPath = "Resources")
            .BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<NotificationStrings>>();
}
