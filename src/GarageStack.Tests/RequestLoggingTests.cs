using GarageStack.Api.Hosting;
using Microsoft.AspNetCore.Http;
using Serilog.Events;

namespace GarageStack.Tests;

public class RequestLogLevelTests
{
    private static LogEventLevel LevelFor(string path, int status, Exception? ex = null, double elapsedMs = 1)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.StatusCode = status;
        return ApiHostingExtensions.RequestLogLevel(context, elapsedMs, ex);
    }

    // Docker probes /health every ten seconds, and monitors poll /api/health.
    [Theory]
    [InlineData("/health", 200)]
    [InlineData("/api/health", 200)]
    [InlineData("/api/health", 503)]
    public void HealthProbes_StayOutOfTheLogFiles(string path, int status)
    {
        Assert.Equal(LogEventLevel.Verbose, LevelFor(path, status));
    }

    // The dashboard and the log viewer poll: their answers were nine lines in ten of a log file.
    [Fact]
    public void EverydaySuccess_IsDebug_RefusalsAndSlowAnswersAreInformation_FailuresError()
    {
        Assert.Equal(LogEventLevel.Debug, LevelFor("/api/vehicles", 200));
        Assert.Equal(LogEventLevel.Debug, LevelFor("/api/vehicles", 304));
        Assert.Equal(LogEventLevel.Information, LevelFor("/api/vehicles", 200, elapsedMs: 2500));
        Assert.Equal(LogEventLevel.Information, LevelFor("/api/auth/login", 401));
        Assert.Equal(LogEventLevel.Information, LevelFor("/api/vehicles", 429));
        Assert.Equal(LogEventLevel.Error, LevelFor("/api/vehicles", 500));
        Assert.Equal(LogEventLevel.Error, LevelFor("/api/health", 200, new InvalidOperationException()));
    }

    // A live-update connection is logged when it ends, with how long it was open as its time.
    [Fact]
    public void AClosingLiveConnection_IsNotASlowRequest()
    {
        Assert.Equal(LogEventLevel.Debug, LevelFor("/hubs/telemetry", 101, elapsedMs: 600_000));
    }

    [Fact]
    public void RequestLine_NamesTheCarByItsLastCharactersOnly()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";

        var properties = ApiHostingExtensions
            .RequestLogProperties(context, "/api/vehicles/FAKEVN00000000001/commands/lock", 12.5, 200)
            .ToDictionary(p => p.Name, p => ((ScalarValue)p.Value).Value);

        Assert.Equal("/api/vehicles/***0001/commands/lock", properties["RequestPath"]);
        Assert.Equal("POST", properties["RequestMethod"]);
        Assert.Equal(200, properties["StatusCode"]);
        Assert.Equal(12.5, properties["Elapsed"]);
    }
}
