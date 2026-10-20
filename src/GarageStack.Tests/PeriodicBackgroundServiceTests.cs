using GarageStack.Worker.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

public class PeriodicBackgroundServiceTests
{
    private sealed class CountingJob(Func<int, bool> fails, ILogger? logger = null)
        : PeriodicBackgroundService(logger ?? NullLogger.Instance)
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        protected override string Name => "Counting job";

        protected override TimeSpan Interval => TimeSpan.FromMilliseconds(10);

        protected override Task RunOnceAsync(CancellationToken ct)
        {
            var run = Interlocked.Increment(ref _runs);
            return fails(run) ? throw new InvalidOperationException("one bad pass") : Task.CompletedTask;
        }
    }

    [Fact]
    public async Task KeepsRunningAfterAFailedPass_AndStopsWithTheHost()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = new CountingJob(run => run == 1);

        await job.StartAsync(ct);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (job.Runs < 3 && DateTime.UtcNow < deadline)
            await Task.Delay(10, ct);
        await job.StopAsync(ct);

        Assert.True(job.Runs >= 3, $"expected the job to keep running after its first pass failed, ran {job.Runs} times");
        Assert.True(job.ExecuteTask?.IsCompletedSuccessfully);
    }

    // A database restart used to log a full stack trace on every pass of a 15-second job.
    [Fact]
    public async Task RunsThatKeepFailing_AreLoggedInFullOnce_ThenTheRecovery()
    {
        var ct = TestContext.Current.CancellationToken;
        var logger = new RecordingLogger<PeriodicBackgroundServiceTests>();
        var job = new CountingJob(run => run <= 3, logger);

        await job.StartAsync(ct);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (job.Runs < 5 && DateTime.UtcNow < deadline)
            await Task.Delay(10, ct);
        await job.StopAsync(ct);

        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.NotNull(error.Exception);
        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Debug));
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("recovered"));
    }
}
