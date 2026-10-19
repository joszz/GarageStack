using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using GarageStack.Data.Demo;
using GarageStack.Data.Repositories;
using GarageStack.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GarageStack.Data.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGarageStackData(this IServiceCollection services, string connectionString)
    {
        // No warning suppression here on purpose: EF's pending-model-changes warning is the
        // only thing that tells a developer they added a property and forgot the migration,
        // before MigrateAsync fails on a real database.
        services.AddDbContext<AppDbContext>(opts => opts.UseNpgsql(connectionString));

        services.AddSharedRepositories();
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<ITelemetryRepository, TelemetryRepository>();
        services.AddScoped<ITripRepository, TripRepository>();
        services.AddScoped<IHousekeepingRepository, HousekeepingRepository>();
        services.AddGarageStackRdw();

        return services;
    }

    public static IServiceCollection AddDemoServices(this IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseInMemoryDatabase("garagestack-demo"));

        // Every repository but these three is the real one, over the in-memory database.
        services.AddSharedRepositories();
        services.AddScoped<IVehicleRepository, DemoVehicleRepository>();
        services.AddSingleton<ITelemetryRepository, DemoTelemetryRepository>();
        services.AddSingleton<ITripRepository, DemoTripRepository>();
        // The RDW card is part of what the demo shows, answered by a stand-in so no plate leaves:
        // on unless RDW_ENABLED says false.
        services.AddSingleton(sp => new RdwOptions
        {
            Enabled = sp.GetRequiredService<IConfiguration>().SwitchOrDefault("Rdw:Enabled", fallback: true),
        });
        services.AddSingleton<IRdwClient, DemoRdwClient>();
        services.AddRdwLookups();

        return services;
    }

    // The repositories demo mode shares with a real deployment, and the upstream clients behind
    // them: the same classes over whichever database the caller registered.
    private static IServiceCollection AddSharedRepositories(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<IPoiRepository, PoiRepository>();
        services.AddScoped<IGeocodeRepository, GeocodeRepository>();
        services.AddScoped<IMapMatchRepository, MapMatchRepository>();
        services.AddScoped<IUserSettingsRepository, UserSettingsRepository>();
        services.AddScoped<IClimateScheduleRepository, ClimateScheduleRepository>();
        services.AddScoped<ICommutePlaceRepository, CommutePlaceRepository>();
        services.AddScoped<IMaintenanceRepository, MaintenanceRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IPushSubscriptionRepository, PushSubscriptionRepository>();
        services.AddScoped<IApiTokenRepository, ApiTokenRepository>();
        return services.AddGarageStackPoiClients();
    }

    // The RDW lookups. Switched off (RDW_ENABLED unset), the client is still there for the API to
    // answer "off" with, but it refuses before sending anything, and nothing calls it anyway.
    private static IServiceCollection AddGarageStackRdw(this IServiceCollection services)
    {
        services.AddHttpClient(RdwApiClient.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UpstreamHttp.UserAgent);
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        services.AddSingleton(sp => RdwOptions.From(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton<IRdwClient, RdwApiClient>();
        return services.AddRdwLookups();
    }

    private static IServiceCollection AddRdwLookups(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IRdwRepository, RdwRepository>();
        services.AddScoped<RdwRefresher>();
        return services;
    }

    // Shared by both GarageStack.Api and GarageStack.Worker (each calls AddGarageStackData
    // or AddDemoServices) so the "ocm"/"overpass" HttpClient config can't drift between processes.
    private static IServiceCollection AddGarageStackPoiClients(this IServiceCollection services)
    {
        services.AddHttpClient(OcmApiClient.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UpstreamHttp.UserAgent);
        });
        services.AddHttpClient(OverpassApiClient.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UpstreamHttp.UserAgent);
            client.Timeout = TimeSpan.FromSeconds(45);
        });
        services.AddHttpClient(NominatimApiClient.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UpstreamHttp.UserAgent);
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        // Matching a long trace is real work upstream, so this one waits longer than the others
        // before giving up on an answer.
        services.AddHttpClient(ValhallaApiClient.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UpstreamHttp.UserAgent);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton(sp => OverpassOptions.From(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton(sp => OpenChargeMapOptions.From(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton(sp => GeocodingOptions.From(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton(sp => MapMatchingOptions.From(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton<OverpassApiClient>();
        services.AddSingleton<OcmApiClient>();
        services.AddSingleton<NominatimApiClient>();
        services.AddSingleton<ValhallaApiClient>();

        return services;
    }
}
