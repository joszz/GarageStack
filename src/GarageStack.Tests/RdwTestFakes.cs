using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Repositories;
using GarageStack.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GarageStack.Tests;

/// <summary>The RDW as a test wants it, counting what it is asked.</summary>
internal sealed class FakeRdwClient : IRdwClient
{
    public RdwRegistration? Registration { get; set; } = new("MG", "MG ZS EV", new DateOnly(2027, 6, 1), true, false);

    public List<RdwRecallStatus> Statuses { get; set; } = [];

    public List<RdwRecallDetails> Details { get; set; } = [];

    /// <summary>Thrown by every call while set.</summary>
    public RdwUnavailableException? Failure { get; set; }

    public int RegistrationRequests { get; private set; }

    public int StatusRequests { get; private set; }

    public List<IReadOnlyCollection<string>> DetailRequests { get; } = [];

    public Task<RdwRegistration?> GetRegistrationAsync(string plate, CancellationToken ct = default)
    {
        RegistrationRequests++;
        return Failure is { } failure ? throw failure : Task.FromResult(Registration);
    }

    public Task<IReadOnlyList<RdwRecallStatus>> GetRecallStatusesAsync(string plate, CancellationToken ct = default)
    {
        StatusRequests++;
        return Failure is { } failure ? throw failure : Task.FromResult<IReadOnlyList<RdwRecallStatus>>(Statuses.ToList());
    }

    public Task<IReadOnlyList<RdwRecallDetails>> GetRecallDetailsAsync(IReadOnlyCollection<string> codes, CancellationToken ct = default)
    {
        DetailRequests.Add(codes);
        return Failure is { } failure
            ? throw failure
            : Task.FromResult<IReadOnlyList<RdwRecallDetails>>(Details.Where(d => codes.Contains(d.Code)).ToList());
    }
}

internal static class RdwTestServices
{
    public const string Vin = "LSJW74098PZ000001";

    /// <summary>The real repositories and refresher over an in-memory database, asking <paramref name="client"/>.</summary>
    public static ServiceProvider Build(IRdwClient client, RdwOptions options, TimeProvider time)
    {
        var root = new InMemoryDatabaseRoot();
        var name = Guid.NewGuid().ToString();
        return new ServiceCollection()
            .AddLogging()
            .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name, root))
            .AddScoped<IVehicleRepository, VehicleRepository>()
            .AddScoped<IRdwRepository, RdwRepository>()
            .AddScoped<INotificationRepository, NotificationRepository>()
            .AddScoped<RdwRefresher>()
            .AddSingleton(client)
            .AddSingleton(options)
            .AddSingleton(time)
            .BuildServiceProvider();
    }

    public static async Task<Vehicle> AddVehicleAsync(ServiceProvider services, string? plate, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var vehicle = new Vehicle { Vin = Vin, LicensePlate = plate };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);
        return vehicle;
    }

    public static async Task<RdwVehicleInfo?> InfoAsync(ServiceProvider services, int vehicleId, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IRdwRepository>().GetAsync(vehicleId, ct);
    }

    public static async Task<RdwVehicleInfo?> RefreshAsync(ServiceProvider services, Vehicle vehicle, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RdwRefresher>().RefreshAsync(vehicle, ct);
    }
}
