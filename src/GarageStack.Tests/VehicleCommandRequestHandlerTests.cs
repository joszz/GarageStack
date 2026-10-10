using GarageStack.Api;
using GarageStack.Api.Services;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

public class VehicleCommandRequestHandlerTests : IDisposable
{
    private const string Vin = "FAKEVN00000000001";

    private readonly ServiceProvider _services;
    private readonly RecordingPublisher _publisher = new();
    private readonly VehicleCommandGate _gate = new(TimeSpan.FromSeconds(30));

    private sealed class RecordingPublisher : IMqttPublisher
    {
        public List<(string Topic, string Payload)> Published { get; } = [];

        public Task PublishAsync(string topic, string payload, CancellationToken ct = default)
        {
            lock (Published) Published.Add((topic, payload));
            return Task.CompletedTask;
        }
    }

    public VehicleCommandRequestHandlerTests()
    {
        var name = Guid.NewGuid().ToString();
        _services = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name))
            .AddScoped<IVehicleRepository, VehicleRepository>()
            .BuildServiceProvider();

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Vehicles.Add(new Vehicle { Vin = Vin, SaicUser = "user" });
        db.SaveChanges();
    }

    public void Dispose() => _services.Dispose();

    private VehicleCommandRequestHandler Handler(TimeSpan? deadline = null) => new(
        _services.GetRequiredService<IServiceScopeFactory>(),
        new VehicleCommandSender(_publisher, _gate),
        NullLogger<VehicleCommandRequestHandler>.Instance,
        deadline);

    private static string Request(string vin, string command = "climate", string value = "on") =>
        new VehicleCommandRequestPayload("req1", vin, command, value).ToJson();

    [Fact]
    public async Task AKnownCar_GetsTheCommandOnItsTopic()
    {
        await Handler().HandleAsync(Request(Vin), TestContext.Current.CancellationToken);

        Assert.Equal([($"saic/user/vehicles/{Vin}/climate/remoteClimateState/set", "on")], _publisher.Published);
    }

    [Fact]
    public async Task AnUnknownCar_GetsNothing()
    {
        await Handler().HandleAsync(Request("FAKEVN00000000009"), TestContext.Current.CancellationToken);

        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task AValueTheGatewayRefuses_IsNotSent()
    {
        await Handler().HandleAsync(Request(Vin, "climate-temperature", "99"), TestContext.Current.CancellationToken);

        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task AnUnreadableRequest_IsDroppedWithoutThrowing()
    {
        await Handler().HandleAsync("{not json", TestContext.Current.CancellationToken);

        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task ACarStillBusyPastTheDeadline_DropsTheRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        // A command from the browser holds the car's gate for the next 30 seconds.
        await _gate.RunAsync(Vin, "doors/locked", () => Task.CompletedTask, ct);

        await Handler(TimeSpan.FromMilliseconds(50)).HandleAsync(Request(Vin), ct);

        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public void Payload_RoundTripsThroughJson()
    {
        var payload = new VehicleCommandRequestPayload("req1", Vin, "seat-left", "2");

        Assert.Equal(payload, VehicleCommandRequestPayload.FromJson(payload.ToJson()));
    }
}
