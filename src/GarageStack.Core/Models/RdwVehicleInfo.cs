using System.Text.Json.Serialization;

namespace GarageStack.Core.Models;

/// <summary>
/// What the RDW last said about a vehicle's registration: when its APK runs out, whether it is
/// insured, and its recalls. One row per vehicle, written by whichever process looked last (the
/// API right after the plate is entered, the Worker on its daily round).
/// </summary>
public class RdwVehicleInfo
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;

    /// <summary>
    /// The plate this was looked up for. A row for any other plate than the vehicle's is about
    /// another car (the plate was changed while a lookup ran) and is not shown.
    /// </summary>
    public string LicensePlate { get; set; } = string.Empty;

    /// <summary>False when the RDW has no vehicle under the plate, a typo far more often than not.</summary>
    public bool Found { get; set; }

    public string? Make { get; set; }

    /// <summary>The trade name, such as "MG EHS PLUG-IN HYBRID".</summary>
    public string? Model { get; set; }

    /// <summary>The last day the current APK is valid, a date on the Dutch calendar.</summary>
    public DateOnly? ApkExpiresOn { get; set; }

    public bool? WamInsured { get; set; }

    /// <summary>The registration's own "open recall" flag, which the per-recall statuses spell out.</summary>
    public bool? OpenRecallIndicator { get; set; }

    /// <summary>The recalls the RDW lists for the plate, as a JSON array of <see cref="RdwRecall"/>.</summary>
    public string RecallsJson { get; set; } = "[]";

    /// <summary>When the RDW last answered. Null until it has.</summary>
    public DateTime? FetchedAt { get; set; }

    /// <summary>Why the last attempt failed, or null when it did not.</summary>
    public string? LastError { get; set; }
}

/// <summary>
/// One recall on the plate. <see cref="Risk"/> is what can happen because of the defect, and
/// <see cref="Severity"/> the RDW's rating of it: ERN (serious), GEM (moderate) or NNB (not rated
/// yet). <see cref="FirstSeenAt"/> is when GarageStack first saw the code, which is what tells a
/// new recall from one it already knows; <see cref="NotifiedAt"/> is when the push for it went out.
/// A recall found by the API's lookup is seen there but pushed by the Worker, which is why the two
/// are kept apart.
/// </summary>
public sealed record RdwRecall(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("defect")] string? Defect,
    [property: JsonPropertyName("risk")] string? Risk,
    [property: JsonPropertyName("repair")] string? Repair,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("firstSeenAt")] DateTime FirstSeenAt,
    [property: JsonPropertyName("notifiedAt")] DateTime? NotifiedAt = null)
{
    /// <summary>RDW's code for a recall the manufacturer still has to repair on this car.</summary>
    public const string OpenStatus = "O";

    /// <summary>RDW's code for a recall the manufacturer reported as repaired.</summary>
    public const string RepairedStatus = "P";

    [JsonIgnore]
    public bool IsOpen => Status == OpenStatus;
}

public static class RdwLimits
{
    public const int NameMaxLength = 128;
    public const int TextMaxLength = 2000;
    public const int SeverityMaxLength = 8;
    public const int ErrorMaxLength = 256;
}
