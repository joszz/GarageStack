using System.Text.Json;
using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// The notification types a device switched off for one car, kept as <c>{"&lt;vehicle id&gt;": ["low-tyre", ...]}</c>
/// beside <see cref="PushSubscription.ExcludedCategories"/>. Both the request and the stored
/// column go through <see cref="Normalize"/>, so an unchanged choice always compares equal to what
/// is stored, whatever order the page listed it in.
/// </summary>
public static class VehicleCategoryExclusions
{
    /// <summary>
    /// More cars than any account has. The map comes from the browser, and every push reads it, so
    /// it is held to a size no real choice reaches.
    /// </summary>
    public const int MaxVehicles = 32;

    /// <summary>
    /// Only the types the checklist offers, once each and in one order; only cars with at least one
    /// type off, by ascending id; and at most <see cref="MaxVehicles"/> of them.
    /// </summary>
    public static Dictionary<int, List<string>> Normalize(IEnumerable<KeyValuePair<int, IReadOnlyList<string>>> requested)
    {
        var normalized = new Dictionary<int, List<string>>();
        foreach (var (vehicleId, groups) in requested.OrderBy(entry => entry.Key))
        {
            if (vehicleId <= 0 || normalized.Count >= MaxVehicles) continue;

            List<string> known =
                [.. (groups ?? []).Where(NotificationCategories.Groups.Contains).Distinct().Order(StringComparer.Ordinal)];
            if (known.Count > 0) normalized[vehicleId] = known;
        }
        return normalized;
    }

    /// <summary>Whether two normalized choices hold the same types for the same cars.</summary>
    public static bool AreEqual(IReadOnlyDictionary<int, List<string>> a, IReadOnlyDictionary<int, List<string>> b) =>
        a.Count == b.Count
        && a.All(entry => b.TryGetValue(entry.Key, out var other) && entry.Value.SequenceEqual(other, StringComparer.Ordinal));

    /// <summary>The column's JSON. Normalized choices serialize the same way every time.</summary>
    public static string ToJson(IReadOnlyDictionary<int, List<string>> exclusions) =>
        JsonSerializer.Serialize(exclusions);

    /// <summary>The choice a column holds. A value that is not the JSON written above reads as no exclusions.</summary>
    public static Dictionary<int, List<string>> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, List<string>>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
