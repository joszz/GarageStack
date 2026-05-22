using GarageStack.Core.Interfaces;
using GarageStack.Data.Repositories;
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

        return services;
    }
}
