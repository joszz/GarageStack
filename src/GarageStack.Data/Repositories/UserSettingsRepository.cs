using System.Text.Json;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GarageStack.Data.Repositories;

// logger is optional (DI always supplies one) so tests can construct this with just a DbContext.
public class UserSettingsRepository(AppDbContext db, ILogger<UserSettingsRepository>? logger = null) : IUserSettingsRepository
{
    // Devices saving the same section at the same moment are the only way a save can be beaten to
    // its row. Each round of a collision has one winner, so this covers as many devices at once.
    private const int MaxAttempts = 5;

    public async Task<IReadOnlyDictionary<string, JsonElement>> GetAsync(string accountKey, CancellationToken ct = default)
    {
        var rows = await db.UserSettings
            .AsNoTracking()
            .Where(s => s.AccountKey == accountKey)
            .Select(s => new { s.Section, s.Json })
            .ToListAsync(ct);

        return rows.ToDictionary(
            row => row.Section,
            row => SettingsDocument.Read(row.Json, ex => LogUnreadable(ex, row.Section)));
    }

    public async Task<SettingsSaveResult> MergeAsync(
        string accountKey,
        string section,
        IReadOnlyDictionary<string, JsonElement> changes,
        CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await MergeAttemptAsync(accountKey, section, changes, ct);
            }
            catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.IsUniqueViolation())
            {
                if (attempt == MaxAttempts)
                {
                    logger?.LogWarning(
                        "Settings section {Section} lost {Attempts} saves in a row to other devices; refusing this one",
                        section, MaxAttempts);
                    return SettingsSaveResult.Conflict;
                }

                // Another device saved this section between the read and the write. Its keys are
                // committed now, so merging again into what it saved keeps both devices' changes.
                logger?.LogDebug("Settings section {Section} was saved concurrently, merging again", section);
                db.ChangeTracker.Clear();
                // A random pause spreads out the devices that collided, so they do not collide again.
                await Task.Delay(Random.Shared.Next(10, 50) * attempt, ct);
            }
        }
    }

    private async Task<SettingsSaveResult> MergeAttemptAsync(
        string accountKey,
        string section,
        IReadOnlyDictionary<string, JsonElement> changes,
        CancellationToken ct)
    {
        var row = await db.UserSettings.FirstOrDefaultAsync(s => s.AccountKey == accountKey && s.Section == section, ct);

        var merged = SettingsDocument.Merge(row?.Json, changes, ex => LogUnreadable(ex, section));
        if (merged.Length > UserSettingsLimits.JsonMaxLength) return SettingsSaveResult.TooLarge;

        if (row is null)
        {
            row = new UserSettings { AccountKey = accountKey, Section = section };
            db.UserSettings.Add(row);
        }

        row.Json = merged;
        row.Version++;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return SettingsSaveResult.Saved;
    }

    private void LogUnreadable(Exception ex, string section) =>
        logger?.LogWarning(ex, "Saved settings section {Section} is unreadable, treating it as empty", section);
}
