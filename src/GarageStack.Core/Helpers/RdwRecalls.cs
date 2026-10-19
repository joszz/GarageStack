using System.Text.Json;
using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Folds the RDW's current recall statuses into the recalls already kept for a vehicle. A code
/// seen before keeps when it was first seen, whether it was pushed, and its details; only its
/// status follows the RDW. That is what keeps a recall from being pushed twice: it is pushed once,
/// while open, and a later change from open to repaired (O to P) is no news.
/// </summary>
public static class RdwRecalls
{
    public static IReadOnlyList<RdwRecall> Parse(string? json) =>
        SafeJson.TryDeserialize<List<RdwRecall>>(json) ?? [];

    public static string Serialize(IReadOnlyList<RdwRecall> recalls) => JsonSerializer.Serialize(recalls);

    /// <summary>The codes in <paramref name="statuses"/> not kept yet: the only ones whose details need asking for.</summary>
    public static IReadOnlyList<string> Unseen(IReadOnlyList<RdwRecallStatus> statuses, IReadOnlyList<RdwRecall> known)
    {
        var knownCodes = known.Select(r => r.Code).ToHashSet(StringComparer.Ordinal);
        return statuses.Select(s => s.Code).Where(code => !knownCodes.Contains(code)).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The recalls as the RDW lists them now, open ones first. A recall the RDW no longer lists is
    /// dropped; a new one is first seen at <paramref name="now"/>, with whatever
    /// <paramref name="details"/> holds for it.
    /// </summary>
    public static IReadOnlyList<RdwRecall> Merge(
        IReadOnlyList<RdwRecall> known,
        IReadOnlyList<RdwRecallStatus> statuses,
        IReadOnlyList<RdwRecallDetails> details,
        DateTime now)
    {
        var knownByCode = known.GroupBy(r => r.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var detailsByCode = details.GroupBy(d => d.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var merged = new List<RdwRecall>();
        foreach (var status in statuses.DistinctBy(s => s.Code, StringComparer.Ordinal))
        {
            if (knownByCode.TryGetValue(status.Code, out var seen))
            {
                merged.Add(seen with { Status = status.Status });
                continue;
            }

            var about = detailsByCode.GetValueOrDefault(status.Code);
            merged.Add(new RdwRecall(status.Code, status.Status, about?.Defect, about?.Risk, about?.Repair, about?.Severity, now));
        }

        return merged
            .OrderByDescending(r => r.IsOpen)
            .ThenBy(r => r.Code, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The open recalls no push has gone out for yet.</summary>
    public static IReadOnlyList<RdwRecall> ToNotify(IReadOnlyList<RdwRecall> recalls) =>
        recalls.Where(r => r.IsOpen && r.NotifiedAt is null).ToList();
}
