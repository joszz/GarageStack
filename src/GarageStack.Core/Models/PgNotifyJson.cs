using System.Text.Json;

namespace GarageStack.Core.Models;

/// <summary>
/// How every pg_notify payload travels between the Api and the Worker: one set of JSON options
/// (web defaults, so camelCase) for all of them. A payload that is not valid JSON throws, and each
/// listener decides what that means for it.
/// </summary>
public static class PgNotifyJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T payload) => JsonSerializer.Serialize(payload, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
