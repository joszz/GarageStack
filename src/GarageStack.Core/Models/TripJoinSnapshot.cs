using System.Text.Json;
using System.Text.Json.Serialization;

namespace GarageStack.Core.Models;

/// <summary>
/// The two trips a join made one, as they were just before it: kept with the joined trip
/// (<see cref="Trip.JoinedFromJson"/>) so the join can be undone. The first trip's own snapshot
/// rides along in <see cref="TripSnapshot.JoinedFromJson"/>, so joins made one after another are
/// undone one at a time, the last first.
/// </summary>
public sealed record TripJoinSnapshot(TripSnapshot First, TripSnapshot Second);

/// <summary>
/// Every stored column of a <see cref="Trip"/> but its vehicle, so undoing a join puts the trip
/// back exactly as it was. A column added to <see cref="Trip"/> belongs here too, in
/// <see cref="From"/> and in <see cref="ApplyTo"/>.
/// </summary>
public sealed record TripSnapshot(
    long Id,
    DateTime StartedAt,
    DateTime EndedAt,
    double DistanceKm,
    int PointCount,
    [property: JsonConverter(typeof(RawJsonConverter))] string PointsJson,
    [property: JsonConverter(typeof(RawJsonConverter))] string? OriginalPointsJson,
    DateTime? EditedAt,
    // By name, as the database keeps it, so a snapshot does not depend on the members' order.
    [property: JsonConverter(typeof(JsonStringEnumConverter<PhoneFusionOutcome>))] PhoneFusionOutcome? PhoneFusion,
    DateTime? PhoneFusionAt,
    double StartLatitude,
    double StartLongitude,
    double EndLatitude,
    double EndLongitude,
    double? MaxSpeedKmh,
    double? AvgMovingSpeedKmh,
    int? MovingSpeedSamples,
    double? OdometerStartKm,
    double? OdometerEndKm,
    double? CounterUsed,
    int? CounterReadings,
    [property: JsonConverter(typeof(RawJsonConverter))] string? StartPlaceJson,
    [property: JsonConverter(typeof(RawJsonConverter))] string? EndPlaceJson,
    TripPurpose? Purpose,
    bool PurposeIsManual,
    string? Notes,
    bool IsHidden,
    DateTime CreatedAt,
    [property: JsonConverter(typeof(RawJsonConverter))] string? JoinedFromJson)
{
    public static TripSnapshot From(Trip trip) => new(
        trip.Id, trip.StartedAt, trip.EndedAt, trip.DistanceKm, trip.PointCount,
        trip.PointsJson, trip.OriginalPointsJson, trip.EditedAt, trip.PhoneFusion, trip.PhoneFusionAt,
        trip.StartLatitude, trip.StartLongitude, trip.EndLatitude, trip.EndLongitude,
        trip.MaxSpeedKmh, trip.AvgMovingSpeedKmh, trip.MovingSpeedSamples,
        trip.OdometerStartKm, trip.OdometerEndKm, trip.CounterUsed, trip.CounterReadings,
        trip.StartPlaceJson, trip.EndPlaceJson, trip.Purpose, trip.PurposeIsManual, trip.Notes, trip.IsHidden,
        trip.CreatedAt, trip.JoinedFromJson);

    /// <summary>Puts every column back onto <paramref name="trip"/>, its id included.</summary>
    public void ApplyTo(Trip trip)
    {
        trip.Id = Id;
        trip.StartedAt = StartedAt;
        trip.EndedAt = EndedAt;
        trip.DistanceKm = DistanceKm;
        trip.PointCount = PointCount;
        trip.PointsJson = PointsJson;
        trip.OriginalPointsJson = OriginalPointsJson;
        trip.EditedAt = EditedAt;
        trip.PhoneFusion = PhoneFusion;
        trip.PhoneFusionAt = PhoneFusionAt;
        trip.StartLatitude = StartLatitude;
        trip.StartLongitude = StartLongitude;
        trip.EndLatitude = EndLatitude;
        trip.EndLongitude = EndLongitude;
        trip.MaxSpeedKmh = MaxSpeedKmh;
        trip.AvgMovingSpeedKmh = AvgMovingSpeedKmh;
        trip.MovingSpeedSamples = MovingSpeedSamples;
        trip.OdometerStartKm = OdometerStartKm;
        trip.OdometerEndKm = OdometerEndKm;
        trip.CounterUsed = CounterUsed;
        trip.CounterReadings = CounterReadings;
        trip.StartPlaceJson = StartPlaceJson;
        trip.EndPlaceJson = EndPlaceJson;
        trip.Purpose = Purpose;
        trip.PurposeIsManual = PurposeIsManual;
        trip.Notes = Notes;
        trip.IsHidden = IsHidden;
        trip.CreatedAt = CreatedAt;
        trip.JoinedFromJson = JoinedFromJson;
    }
}

/// <summary>
/// Writes a column that already holds JSON (the fixes, an address, an earlier snapshot) into the
/// snapshot as JSON rather than as a string, so it is not escaped again at every join in a row.
/// </summary>
public sealed class RawJsonConverter : JsonConverter<string?>
{
    public override bool HandleNull => true;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.GetRawText();
    }

    // Only ever this app's own JSON; one that does not parse (an address saved unreadable) is kept
    // as none, which the trip log looks up again.
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            document.WriteTo(writer);
        }
        catch (JsonException)
        {
            writer.WriteNullValue();
        }
    }
}
