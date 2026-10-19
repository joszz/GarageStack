using System.Text;

namespace GarageStack.Core.Helpers;

/// <summary>
/// A Dutch registration as the RDW keys it: upper case, without the dashes and spaces it is
/// written with. "ab-123-c" and "AB 123 C" are the same car, AB123C.
/// </summary>
public static class LicensePlate
{
    // Dutch plates are six characters; a few older and special series run a little longer.
    public const int MaxLength = 8;

    /// <summary>
    /// The plate in RDW form, or null when nothing is left once the separators are gone. The
    /// result is not checked: <see cref="IsValid"/> says whether it can be one.
    /// </summary>
    public static string? Normalize(string? plate)
    {
        if (plate is null) return null;

        var normalized = new StringBuilder(plate.Length);
        foreach (var c in plate)
        {
            if (c == '-' || char.IsWhiteSpace(c)) continue;
            normalized.Append(char.ToUpperInvariant(c));
        }
        return normalized.Length == 0 ? null : normalized.ToString();
    }

    /// <summary>Whether a normalized plate can be a registration: letters and digits only, eight at most.</summary>
    public static bool IsValid(string? normalized) =>
        normalized is { Length: > 0 and <= MaxLength } && normalized.All(char.IsAsciiLetterOrDigit);
}
