using System.Diagnostics.CodeAnalysis;

namespace GarageStack.Core.Helpers;

public static class TextExtensions
{
    /// <summary>The text cut to at most <paramref name="maxLength"/> characters, for a column or a log line with a limit.</summary>
    [return: NotNullIfNotNull(nameof(text))]
    public static string? Truncate(this string? text, int maxLength) =>
        text is { Length: var length } && length > maxLength ? text[..maxLength] : text;
}
