using GarageStack.Core.Helpers;

namespace GarageStack.Tests;

public class NotificationCooldownGateTests
{
    [Fact]
    public async Task ShouldNotify_FirstCall_NoDbMatch_ReturnsTrue()
    {
        var gate = new NotificationCooldownGate(TimeSpan.FromHours(1));

        var result = await gate.ShouldNotifyAsync("VIN1", "low-tyre", () => Task.FromResult(false));

        Assert.True(result);
    }

    [Fact]
    public async Task ShouldNotify_SecondCallWithinCooldown_ReturnsFalse_WithoutCheckingDbAgain()
    {
        var gate = new NotificationCooldownGate(TimeSpan.FromHours(1));
        var dbChecked = false;

        await gate.ShouldNotifyAsync("VIN1", "low-tyre", () => Task.FromResult(false));
        var result = await gate.ShouldNotifyAsync("VIN1", "low-tyre", () =>
        {
            dbChecked = true;
            return Task.FromResult(false);
        });

        Assert.False(result);
        Assert.False(dbChecked);
    }

    [Fact]
    public async Task ShouldNotify_OutsideCooldown_ChecksDbAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = new NotificationCooldownGate(TimeSpan.FromMilliseconds(10));

        await gate.ShouldNotifyAsync("VIN1", "low-tyre", () => Task.FromResult(false));
        await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
        var result = await gate.ShouldNotifyAsync("VIN1", "low-tyre", () => Task.FromResult(false));

        Assert.True(result);
    }

    [Fact]
    public async Task ShouldNotify_DbHasRecentMatch_ReturnsFalse_AndCachesResultWithoutFurtherDbChecks()
    {
        var gate = new NotificationCooldownGate(TimeSpan.FromHours(1));
        var dbCheckCount = 0;

        var first = await gate.ShouldNotifyAsync("VIN1", "engine-start", () =>
        {
            dbCheckCount++;
            return Task.FromResult(true);
        });
        var second = await gate.ShouldNotifyAsync("VIN1", "engine-start", () =>
        {
            dbCheckCount++;
            return Task.FromResult(true);
        });

        Assert.False(first);
        Assert.False(second);
        // The DB hit on the first call is cached as if we'd notified, so the second call
        // never re-checks the DB - this is what lets a restart avoid hammering the DB on
        // every check for a condition another service already alerted on.
        Assert.Equal(1, dbCheckCount);
    }

    [Fact]
    public async Task ShouldNotify_DifferentCategories_AreIndependent()
    {
        var gate = new NotificationCooldownGate(TimeSpan.FromHours(1));

        await gate.ShouldNotifyAsync("VIN1", "low-tyre", () => Task.FromResult(false));
        var result = await gate.ShouldNotifyAsync("VIN1", "high-tyre", () => Task.FromResult(false));

        Assert.True(result);
    }

    [Fact]
    public async Task ShouldNotify_DifferentVins_AreIndependent()
    {
        var gate = new NotificationCooldownGate(TimeSpan.FromHours(1));

        await gate.ShouldNotifyAsync("VIN1", "low-tyre", () => Task.FromResult(false));
        var result = await gate.ShouldNotifyAsync("VIN2", "low-tyre", () => Task.FromResult(false));

        Assert.True(result);
    }

    // ── Cutoff-computing overload (used by MaintenanceCheckService/PushNotificationCheckService
    // via AppDbContext.WasNotificationSentSinceAsync) ──────────────────────────────────────────

    [Fact]
    public async Task ShouldNotifyWithCutoff_PassesCooldownDerivedCutoff_ToCallback()
    {
        var cooldown = TimeSpan.FromHours(1);
        var gate = new NotificationCooldownGate(cooldown);
        var before = DateTime.UtcNow;

        DateTime? receivedCutoff = null;
        await gate.ShouldNotifyAsync("VIN1", "low-tyre", cutoff =>
        {
            receivedCutoff = cutoff;
            return Task.FromResult(false);
        });

        Assert.NotNull(receivedCutoff);
        // The cutoff passed to the callback is "now minus cooldown", computed at call time -
        // assert it falls within the window this test itself ran in, rather than pinning an
        // exact timestamp.
        Assert.InRange(receivedCutoff!.Value, before - cooldown, DateTime.UtcNow - cooldown);
    }

    [Fact]
    public async Task ShouldNotifyWithCutoff_DbHasRecentMatch_ReturnsFalse()
    {
        var gate = new NotificationCooldownGate(TimeSpan.FromHours(1));

        var result = await gate.ShouldNotifyAsync("VIN1", "engine-start", _ => Task.FromResult(true));

        Assert.False(result);
    }
}
