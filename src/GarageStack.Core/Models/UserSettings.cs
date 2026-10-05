using System.Collections.Frozen;

namespace GarageStack.Core.Models;

/// <summary>
/// One section ("ui", "dashboard" or "map") of one account's settings: a JSON object of top-level
/// keys, as the browser keeps them. Kept on the server so an account's settings follow it to every
/// device. The server does not interpret the values; the browser checks each one as it reads it,
/// just as it does with its own stored copy.
/// </summary>
public class UserSettings
{
    public long Id { get; set; }

    /// <summary>
    /// Whose settings these are: a hash of the sign-in method and the account's subject, so the
    /// table holds no user name or email address.
    /// </summary>
    public string AccountKey { get; set; } = string.Empty;

    public string Section { get; set; } = string.Empty;

    /// <summary>The section's settings as a JSON object.</summary>
    public string Json { get; set; } = "{}";

    /// <summary>Bumped on every save, so two devices saving at once cannot drop each other's keys.</summary>
    public int Version { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class UserSettingsLimits
{
    /// <summary>The sections the browser keeps. A fixed list, so an account cannot add rows without end.</summary>
    public static readonly FrozenSet<string> Sections = FrozenSet.Create(StringComparer.Ordinal, "ui", "dashboard", "map");

    public const int SectionMaxLength = 16;

    /// <summary>A SHA-256 hash in hex.</summary>
    public const int AccountKeyLength = 64;

    public const int KeyMaxLength = 64;

    public const int MaxKeys = 64;

    /// <summary>
    /// The most one section may hold once a change is merged in: about ten times what the largest,
    /// the dashboard's card layout, needs.
    /// </summary>
    public const int JsonMaxLength = 16 * 1024;
}

public enum SettingsSaveResult
{
    Saved,

    /// <summary>The section would grow past <see cref="UserSettingsLimits.JsonMaxLength"/>; nothing was saved.</summary>
    TooLarge,

    /// <summary>Other devices kept saving the same section first; nothing was saved, and the browser sends it again.</summary>
    Conflict,
}
