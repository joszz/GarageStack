using System.Text.Json;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Deserializes JSON without throwing on malformed input. Used for data that originates
/// outside this app's control (OSM/OCM API responses, free-form config blobs) where a parse
/// failure should fall back to a default value instead of taking down the caller.
/// </summary>
public static class SafeJson
{
    public static T? TryDeserialize<T>(string? json, Action<Exception>? onError = null)
    {
        if (json is null) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            return default;
        }
    }
}
