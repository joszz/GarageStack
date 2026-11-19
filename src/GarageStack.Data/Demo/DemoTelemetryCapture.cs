using System.Globalization;
using System.Reflection;
using System.Text;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Data.Demo;

internal static class DemoTelemetryCapture
{
    private static readonly Lazy<IReadOnlyList<TelemetrySnapshot>?> _snapshots = new(LoadIfPresent);

    public static IReadOnlyList<TelemetrySnapshot>? Snapshots => _snapshots.Value;

    public static TelemetrySnapshot MergeLatest(IReadOnlyList<TelemetrySnapshot> snapshots)
    {
        var latest = snapshots[^1].Clone();
        foreach (var snapshot in snapshots.Reverse().Skip(1))
            TelemetryFields.ApplyFirstNonNullFields(latest, snapshot);
        return latest;
    }

    private static IReadOnlyList<TelemetrySnapshot>? LoadIfPresent()
    {
        var configuredPath = Environment.GetEnvironmentVariable("DEMO_TELEMETRY_CSV");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var path = Path.GetFullPath(configuredPath);
            if (!File.Exists(path))
                return null;
            return Load(path);
        }

        var roots = new[] { Environment.CurrentDirectory, AppContext.BaseDirectory }
            .SelectMany(Ancestors)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var telemetryDirectory = Path.Combine(root, "telemetry");
            if (!Directory.Exists(telemetryDirectory)) continue;

            var canonicalFile = Path.Combine(telemetryDirectory, "telemetry.csv");
            if (File.Exists(canonicalFile))
                return Load(canonicalFile);

            var files = Directory.EnumerateFiles(telemetryDirectory, "telemetry.csv", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (files.Length == 0) continue;
            if (files.Length > 1)
                throw new InvalidOperationException(
                    $"Found multiple demo telemetry CSV files under '{telemetryDirectory}'. Set DEMO_TELEMETRY_CSV to select one.");
            return Load(files[0]);
        }

        return null;
    }

    private static IEnumerable<string> Ancestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            yield return directory.FullName;
    }

    private static IReadOnlyList<TelemetrySnapshot> Load(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var records = ReadRecords(reader).GetEnumerator();
        if (!records.MoveNext())
            throw new InvalidDataException($"Demo telemetry CSV '{path}' is empty.");

        var headers = records.Current;
        var properties = typeof(TelemetrySnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name != nameof(TelemetrySnapshot.Vehicle))
            .ToDictionary(property => property.Name, StringComparer.Ordinal);
        if (!headers.Contains(nameof(TelemetrySnapshot.RecordedAt), StringComparer.Ordinal))
            throw new InvalidDataException($"Demo telemetry CSV '{path}' is missing the RecordedAt column.");
        var snapshots = new List<TelemetrySnapshot>();
        var rowNumber = 1;

        while (records.MoveNext())
        {
            rowNumber++;
            var cells = records.Current;
            if (cells.Length != headers.Length)
                throw new InvalidDataException(
                    $"Demo telemetry CSV '{path}' row {rowNumber} has {cells.Length} cells; expected {headers.Length}.");

            var snapshot = new TelemetrySnapshot();
            for (var column = 0; column < headers.Length; column++)
            {
                if (!properties.TryGetValue(headers[column], out var property))
                    continue;

                var cell = cells[column];
                if (cell.Length == 0)
                    continue;

                try
                {
                    property.SetValue(snapshot, Parse(cell, property.PropertyType));
                }
                catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
                {
                    throw new InvalidDataException(
                        $"Demo telemetry CSV '{path}' row {rowNumber}, column '{headers[column]}' has an invalid value.",
                        exception);
                }
            }
            snapshots.Add(snapshot);
        }

        if (snapshots.Count == 0)
            throw new InvalidDataException($"Demo telemetry CSV '{path}' has a header but no data rows.");
        return snapshots.OrderBy(snapshot => snapshot.RecordedAt).ToArray();
    }

    private static object Parse(string value, Type propertyType)
    {
        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (type == typeof(string)) return value;
        if (type == typeof(bool)) return bool.Parse(value);
        if (type == typeof(DateTime)) return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (type == typeof(int)) return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (type == typeof(long)) return long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (type == typeof(double)) return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        throw new InvalidDataException($"Unsupported demo telemetry column type '{propertyType}'.");
    }

    private static IEnumerable<string[]> ReadRecords(TextReader reader)
    {
        var text = reader.ReadToEnd();
        var cells = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character != '"')
                {
                    field.Append(character);
                    continue;
                }
                if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = false;
                }
                continue;
            }

            switch (character)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    break;
                case ',':
                    cells.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    if (index + 1 < text.Length && text[index + 1] == '\n') index++;
                    cells.Add(field.ToString());
                    field.Clear();
                    yield return [.. cells];
                    cells.Clear();
                    break;
                case '\n':
                    cells.Add(field.ToString());
                    field.Clear();
                    yield return [.. cells];
                    cells.Clear();
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }

        if (quoted)
            throw new InvalidDataException("The demo telemetry CSV contains an unterminated quoted field.");
        if (field.Length > 0 || cells.Count > 0)
        {
            cells.Add(field.ToString());
            yield return [.. cells];
        }
    }
}
