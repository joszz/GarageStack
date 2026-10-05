using System.Buffers;
using System.Text;
using System.Text.Json;

namespace GarageStack.Core.Helpers;

/// <summary>
/// A settings section as stored: a JSON object whose top-level keys a device saves a few at a
/// time. Merging by key is what lets two devices that changed different settings both keep theirs.
/// Values are copied as they came, never re-parsed into a model, since only the browser knows
/// what they mean.
/// </summary>
public static class SettingsDocument
{
    private static readonly JsonElement Empty = ParseEmpty();

    /// <summary>
    /// The stored section with <paramref name="changes"/> written over it. Keys the changes leave
    /// out keep their stored values.
    /// </summary>
    public static string Merge(
        string? stored,
        IReadOnlyDictionary<string, JsonElement> changes,
        Action<Exception>? onUnreadable = null)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in Read(stored, onUnreadable).EnumerateObject())
            {
                if (!changes.ContainsKey(property.Name)) property.WriteTo(writer);
            }

            foreach (var (key, value) in changes)
            {
                writer.WritePropertyName(key);
                value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// The stored section as a JSON object. One that cannot be read (edited by hand in the
    /// database, say) counts as empty, so it neither breaks the page nor blocks the next save.
    /// </summary>
    public static JsonElement Read(string? stored, Action<Exception>? onUnreadable = null)
    {
        if (string.IsNullOrEmpty(stored)) return Empty;

        try
        {
            using var document = JsonDocument.Parse(stored);
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document.RootElement.Clone();
            onUnreadable?.Invoke(new JsonException("The stored settings are not a JSON object"));
        }
        catch (JsonException ex)
        {
            onUnreadable?.Invoke(ex);
        }

        return Empty;
    }

    private static JsonElement ParseEmpty()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}
