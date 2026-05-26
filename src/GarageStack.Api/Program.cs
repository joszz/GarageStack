using System.Text.Json.Serialization;
using GarageStack.Api.Endpoints;
using GarageStack.Api.Services;
using GarageStack.Core.Interfaces;
using GarageStack.Data;
using GarageStack.Data.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using System.Text;
using System.Threading.RateLimiting;

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

    var jwtSecret = builder.Configuration["Jwt:Secret"]
        ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
    var jwtSecretBytes = Encoding.UTF8.GetBytes(jwtSecret);
    if (jwtSecretBytes.Length < 32)
        throw new InvalidOperationException("Jwt:Secret must be at least 32 bytes.");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(jwtSecretBytes),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
            };
        });
    builder.Services.AddAuthorization();

    builder.Services.AddRateLimiter(opts =>
    {
        opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        opts.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ip,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 120,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
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
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapOpenApi();
    app.MapScalarApiReference(opts => opts.WithTitle("GarageStack API"));

    app.MapAuthEndpoints();
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
