using System.Text.Json;
using GarageStack.Core.Models;

namespace GarageStack.Core.Interfaces;

/// <summary>
/// Keeps each account's settings, a JSON object per section, so they follow the account to every
/// device. Accounts are addressed by their account key, never by name.
/// </summary>
public interface IUserSettingsRepository
{
    /// <summary>Every section the account has saved, by name. A section never saved is absent.</summary>
    Task<IReadOnlyDictionary<string, JsonElement>> GetAsync(string accountKey, CancellationToken ct = default);

    /// <summary>
    /// Writes <paramref name="changes"/> over the saved section, keeping the keys they leave out.
    /// Safe against another device saving the same section at the same moment: both keep their keys.
    /// </summary>
    Task<SettingsSaveResult> MergeAsync(
        string accountKey,
        string section,
        IReadOnlyDictionary<string, JsonElement> changes,
        CancellationToken ct = default);
}
