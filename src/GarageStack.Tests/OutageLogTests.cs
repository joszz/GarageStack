using GarageStack.Core.Helpers;
using Microsoft.Extensions.Logging;

namespace GarageStack.Tests;

// The broker or the database going down for an hour used to write an Error every 5 seconds.
public class OutageLogTests
{
    private readonly RecordingLogger<OutageLogTests> _logger = new();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero));

    private OutageLog Log() => new(_logger, "MQTT broker connection", TimeSpan.FromMinutes(5), _time);

    private int Count(LogLevel level) => _logger.Entries.Count(e => e.Level == level);

    [Fact]
    public void AnOutage_IsLoggedInFullOnce_ThenRemindedEveryFewMinutes()
    {
        var outage = Log();

        // An hour of retries every 5 seconds.
        for (var attempt = 0; attempt < 720; attempt++)
        {
            outage.Failed(new IOException("Connection refused"));
            _time.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(1, Count(LogLevel.Error));
        Assert.Equal(11, Count(LogLevel.Warning));
        Assert.NotNull(_logger.Entries.Single(e => e.Level == LogLevel.Error).Exception);
    }

    [Fact]
    public void TheRecovery_IsLoggedOnce_AndANewOutageStartsOver()
    {
        var outage = Log();

        outage.Failed(new IOException("Connection refused"));
        _time.Advance(TimeSpan.FromMinutes(2));
        outage.Recovered();
        outage.Recovered();
        outage.Failed(new IOException("Connection refused"));

        Assert.Equal(1, Count(LogLevel.Information));
        Assert.Contains("00:02:00", _logger.Entries.Single(e => e.Level == LogLevel.Information).Message);
        Assert.Equal(2, Count(LogLevel.Error));
    }

    [Fact]
    public void ASuccessWithoutAnOutage_SaysNothing()
    {
        Log().Recovered();

        Assert.Empty(_logger.Entries);
    }
}
