using System.Text.Json.Serialization;
using GarageStack.Api.Endpoints;
using GarageStack.Api.Services;
using GarageStack.Core.Interfaces;
using GarageStack.Data;
using GarageStack.Data.Extensions;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((_, config) => config
        .ReadFrom.Configuration(builder.Configuration)
        .WriteTo.Console()
        .WriteTo.File(
            "logs/api-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30));

    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection is not configured.");

    builder.Services.AddGarageStackData(connectionString);
    builder.Services.AddSingleton<MqttPublisher>();
    builder.Services.AddSingleton<IMqttPublisher>(sp => sp.GetRequiredService<MqttPublisher>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttPublisher>());
    builder.Services.AddOpenApi();
    builder.Services.ConfigureHttpJsonOptions(opts =>
    {
        opts.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        opts.SerializerOptions.Converters.Add(new FiniteDoubleConverter());
    });

    builder.Services.AddRateLimiter(opts =>
    {
        opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        opts.AddFixedWindowLimiter("fixed", o =>
        {
            o.Window = TimeSpan.FromMinutes(1);
            o.PermitLimit = 120;
        });
    });

    builder.Services.AddCors(opts =>
        opts.AddDefaultPolicy(p =>
        {
            if (builder.Environment.IsDevelopment())
                p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod();
            else
                p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
                 .AllowAnyHeader()
                 .AllowAnyMethod();
        }));

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
    {
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync("{\"error\":\"Internal server error\"}");
    }));

    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    });
    app.UseSerilogRequestLogging();
    app.UseCors();
    app.UseRateLimiter();

    app.MapOpenApi();
    app.MapScalarApiReference(opts => opts.WithTitle("GarageStack API"));

    app.MapVehicleEndpoints();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
