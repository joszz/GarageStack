using GarageStack.Core.Helpers;

namespace GarageStack.Api.Services;

/// <param name="Enabled">UPDATE_CHECK_ENABLED: false keeps GarageStack from asking GitHub.</param>
/// <param name="CurrentVersion">The version this build was stamped with, or null when it has none.</param>
public sealed record UpdateCheckOptions(bool Enabled, ReleaseVersion? CurrentVersion);

/// <summary>What the footer shows: whether a newer release exists, and where to read about it.</summary>
public sealed record UpdateInfo(bool UpdateAvailable, string? LatestVersion, string? ReleaseUrl)
{
    public static readonly UpdateInfo None = new(false, null, null);
}

/// <summary>
/// Asks GitHub once a day whether a newer GarageStack release exists. A build without a version
/// (from source without APP_VERSION) never asks, since nothing can be compared with it.
/// </summary>
public sealed class UpdateCheckService(
    GitHubReleaseClient releases,
    UpdateCheckOptions options,
    ILogger<UpdateCheckService> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    // A failed check (no internet yet after a power cut, say) is tried again sooner.
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

    private volatile UpdateInfo _latest = UpdateInfo.None;

    /// <summary>The answer of the last check that reached GitHub.</summary>
    public UpdateInfo Latest => _latest;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Update check is switched off (UPDATE_CHECK_ENABLED)");
            return;
        }

        if (options.CurrentVersion is not { IsKnown: true } current)
        {
            logger.LogInformation("Update check skipped: this build carries no version (set APP_VERSION when building)");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            bool reached;
            try
            {
                reached = await CheckAsync(current, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // An exception out of a BackgroundService stops the whole host; a failed update
                // check must never take the API down with it.
                logger.LogWarning(ex, "Update check failed");
                reached = false;
            }

            try
            {
                await Task.Delay(reached ? Interval : RetryInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <returns>Whether GitHub answered; the previous answer stays when it did not.</returns>
    internal async Task<bool> CheckAsync(ReleaseVersion current, CancellationToken ct)
    {
        if (await releases.GetLatestAsync(ct) is not { } latest) return false;

        _latest = Evaluate(current, latest);
        if (_latest.UpdateAvailable)
            logger.LogInformation("GarageStack {Latest} is available (running {Current})", latest, current);
        return true;
    }

    public static UpdateInfo Evaluate(ReleaseVersion current, ReleaseVersion latest) =>
        current.IsOlderThan(latest)
            ? new UpdateInfo(true, latest.ToString(), GitHubReleaseClient.ReleasePageUrl(latest))
            : UpdateInfo.None;
}
