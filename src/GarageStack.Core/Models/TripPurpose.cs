using System.Text.Json.Serialization;

namespace GarageStack.Core.Models;

/// <summary>
/// What a trip was for, as the trip log records it. Kept apart from private driving because tax
/// rules treat them differently: a Dutch company car counts commuting as business, while a
/// mileage allowance for a private car usually does not.
/// </summary>
[JsonConverter(typeof(TripPurposeJsonConverter))]
public enum TripPurpose
{
    [JsonStringEnumMemberName("business")]
    Business,

    [JsonStringEnumMemberName("commute")]
    Commute,

    [JsonStringEnumMemberName("private")]
    Private,
}

/// <summary>
/// Reads and writes a purpose by name only. The stock string converter also accepts a number, and
/// would let a request store a purpose outside the three.
/// </summary>
public sealed class TripPurposeJsonConverter()
    : JsonStringEnumConverter<TripPurpose>(namingPolicy: null, allowIntegerValues: false);
