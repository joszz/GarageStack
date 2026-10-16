using GarageStack.Api.Hosting;
using Microsoft.AspNetCore.Http;
using Serilog.Events;

namespace GarageStack.Tests;

public class RequestLogLevelTests
{
    private static LogEventLevel LevelFor(string path, int status, Exception? ex = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.StatusCode = status;
        return ApiHostingExtensions.RequestLogLevel(context, 1, ex);
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

    [Fact]
    public void OrdinaryRequests_AreLoggedAsBefore()
    {
        Assert.Equal(LogEventLevel.Information, LevelFor("/api/vehicles", 200));
        Assert.Equal(LogEventLevel.Error, LevelFor("/api/vehicles", 500));
        Assert.Equal(LogEventLevel.Error, LevelFor("/api/health", 200, new InvalidOperationException()));
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
