using GarageStack.Worker.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

public class PeriodicBackgroundServiceTests
{
    private sealed class CountingJob(int failOnRun) : PeriodicBackgroundService(NullLogger.Instance)
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        protected override string Name => "Counting job";

        protected override TimeSpan Interval => TimeSpan.FromMilliseconds(10);

        protected override Task RunOnceAsync(CancellationToken ct)
        {
            var run = Interlocked.Increment(ref _runs);
            return run == failOnRun ? throw new InvalidOperationException("one bad pass") : Task.CompletedTask;
        }
    }

    [Fact]
    public async Task KeepsRunningAfterAFailedPass_AndStopsWithTheHost()
    {
        var ct = TestContext.Current.CancellationToken;
        var job = new CountingJob(failOnRun: 1);

        await job.StartAsync(ct);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (job.Runs < 3 && DateTime.UtcNow < deadline)
            await Task.Delay(10, ct);
        await job.StopAsync(ct);

        Assert.True(job.Runs >= 3, $"expected the job to keep running after its first pass failed, ran {job.Runs} times");
        Assert.True(job.ExecuteTask?.IsCompletedSuccessfully);
    }
}
