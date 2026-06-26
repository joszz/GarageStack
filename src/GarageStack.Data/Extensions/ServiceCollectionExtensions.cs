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

        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<ITelemetryRepository, TelemetryRepository>();
        services.AddScoped<IPoiRepository, PoiRepository>();
        services.AddSingleton<OverpassApiClient>();
        services.AddSingleton<OcmApiClient>();

        return services;
    }

    public static IServiceCollection AddDemoServices(this IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseInMemoryDatabase("garagestack-demo"));

        services.AddScoped<IVehicleRepository, DemoVehicleRepository>();
        services.AddSingleton<ITelemetryRepository, DemoTelemetryRepository>();
        services.AddSingleton<IPushSender, DemoPushSender>();
        services.AddScoped<IPoiRepository, PoiRepository>();
        services.AddSingleton<OverpassApiClient>();
        services.AddSingleton<OcmApiClient>();

        return services;
    }
}
