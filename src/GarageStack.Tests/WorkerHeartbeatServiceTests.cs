using GarageStack.Data;
using GarageStack.Worker.Mqtt;
using GarageStack.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

public class WorkerHeartbeatServiceTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"garagestack-heartbeat-test-{Guid.NewGuid():N}");

    public void Dispose() => File.Delete(_file);

    private static IServiceScopeFactory InMemoryDatabase()
    {
        var name = Guid.NewGuid().ToString();
        return new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name))
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
    }

    private WorkerHeartbeatService Service(IServiceScopeFactory scopes, bool mqttConnected)
    {
        var mqtt = new MqttConnectionState();
        mqtt.Set(mqttConnected);
        return new WorkerHeartbeatService(NullLogger<WorkerHeartbeatService>.Instance, scopes, mqtt) { FilePath = _file };
    }

    [Fact]
    public async Task Beat_SubscribedAndDatabaseReachable_TouchesTheFile()
    {
        await Service(InMemoryDatabase(), mqttConnected: true).BeatAsync(TestContext.Current.CancellationToken);

        Assert.True(File.Exists(_file));
    }

    [Fact]
    public async Task Beat_BrokerDisconnected_LeavesTheFileToAge()
    {
        await Service(InMemoryDatabase(), mqttConnected: false).BeatAsync(TestContext.Current.CancellationToken);

        Assert.False(File.Exists(_file));
    }

    // FakeServiceScopeFactory resolves no database, which is what an unreachable one looks like here.
    [Fact]
    public async Task Beat_DatabaseUnreachable_LeavesTheFileToAge()
    {
        await Service(new FakeServiceScopeFactory(), mqttConnected: true).BeatAsync(TestContext.Current.CancellationToken);

        Assert.False(File.Exists(_file));
    }
}
