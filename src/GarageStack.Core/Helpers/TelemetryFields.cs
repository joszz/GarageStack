using System.Linq.Expressions;
using System.Reflection;
using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// The fields of a <see cref="TelemetrySnapshot"/> that carry telemetry, and the two ways rows are
/// merged field by field. Every merge goes through here (the Worker folding a poll into one row,
/// the dashboard's view of the current state, and compaction of old rows), so a new telemetry field
/// only needs to be added to the model, not hand-copied into each merge loop.
/// </summary>
public static class TelemetryFields
{
    // Identity and bookkeeping: never merged.
    private static readonly HashSet<string> NonMergeable =
    [
        nameof(TelemetrySnapshot.Id), nameof(TelemetrySnapshot.VehicleId),
        nameof(TelemetrySnapshot.Vehicle), nameof(TelemetrySnapshot.RecordedAt),
        nameof(TelemetrySnapshot.RawTopic),
    ];

    /// <summary>Every property that carries telemetry, in declaration order.</summary>
    public static readonly IReadOnlyList<PropertyInfo> Mergeable = typeof(TelemetrySnapshot)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && !NonMergeable.Contains(p.Name))
        .ToArray();

    // Compiled expression-tree accessors instead of live reflection (PropertyInfo.GetValue/
    // SetValue): merging runs on every MQTT message, multiple times per second per vehicle, and
    // reflection's per-call overhead is avoidable since the property set is fixed at startup.
    // Built once here, then invoked like a regular delegate call from then on.
    private sealed record PropertyAccessor(string Name, Func<TelemetrySnapshot, object?> Get, Action<TelemetrySnapshot, object?> Set);

    private static PropertyAccessor BuildAccessor(PropertyInfo prop)
    {
        var instance = Expression.Parameter(typeof(TelemetrySnapshot), "instance");
        var value = Expression.Parameter(typeof(object), "value");

        var getter = Expression.Lambda<Func<TelemetrySnapshot, object?>>(
            Expression.Convert(Expression.Property(instance, prop), typeof(object)),
            instance).Compile();

        var setter = Expression.Lambda<Action<TelemetrySnapshot, object?>>(
            Expression.Assign(
                Expression.Property(instance, prop),
                Expression.Convert(value, prop.PropertyType)),
            instance, value).Compile();

        return new PropertyAccessor(prop.Name, getter, setter);
    }

    private static readonly PropertyAccessor[] Accessors = [.. Mergeable.Select(BuildAccessor)];

    /// <summary>Overwrites every field on <paramref name="target"/> with the non-null value from <paramref name="source"/>, if any.</summary>
    public static void ApplyNonNullFields(TelemetrySnapshot target, TelemetrySnapshot source)
    {
        foreach (var prop in Accessors)
        {
            var value = prop.Get(source);
            if (value is not null) prop.Set(target, value);
        }
    }

    /// <summary>Fills any still-empty field on <paramref name="target"/> from <paramref name="source"/>, leaving already-set fields untouched.</summary>
    public static void ApplyFirstNonNullFields(TelemetrySnapshot target, TelemetrySnapshot source, ISet<string>? skip = null)
    {
        foreach (var prop in Accessors)
        {
            if (skip is not null && skip.Contains(prop.Name)) continue;
            if (prop.Get(target) is not null) continue;
            var value = prop.Get(source);
            if (value is not null) prop.Set(target, value);
        }
    }
}
