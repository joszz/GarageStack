using GarageStack.Core.Interfaces;
using GarageStack.Data.Demo;
using GarageStack.Data.Repositories;
using GarageStack.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GarageStack.Data.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGarageStackData(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseNpgsql(connectionString)
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        services.AddMemoryCache();
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<ITelemetryRepository, TelemetryRepository>();
        services.AddScoped<IPoiRepository, PoiRepository>();
        services.AddGarageStackPoiClients();

        return services;
    }

    public static IServiceCollection AddDemoServices(this IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseInMemoryDatabase("garagestack-demo"));

        services.AddMemoryCache();
        services.AddScoped<IVehicleRepository, DemoVehicleRepository>();
        services.AddSingleton<ITelemetryRepository, DemoTelemetryRepository>();
        services.AddSingleton<IPushSender, DemoPushSender>();
        services.AddScoped<IPoiRepository, PoiRepository>();
        services.AddGarageStackPoiClients();

        return services;
    }

    // Shared by both GarageStack.Api and GarageStack.Worker (each calls AddGarageStackData
    // or AddDemoServices) so the "ocm"/"overpass" HttpClient config can't drift between processes.
    private static IServiceCollection AddGarageStackPoiClients(this IServiceCollection services)
    {
        services.AddHttpClient("ocm", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GarageStack/1.0");
        });
        services.AddHttpClient("overpass", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GarageStack/1.0");
            client.Timeout = TimeSpan.FromSeconds(45);
        });
        services.AddSingleton<OverpassApiClient>();
        services.AddSingleton<OcmApiClient>();

        return services;
    }
}
