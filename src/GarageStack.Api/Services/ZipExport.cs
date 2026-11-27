using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GarageStack.Core.Models;

namespace GarageStack.Api.Services;

/// <summary>
/// What the downloads built as a zip share: the data export (<see cref="DataExport"/>) and the
/// diagnostics (<see cref="DiagnosticsExport"/>). JSON entries, CSV entries a spreadsheet opens,
/// text entries, and the temporary file the zip is built in before it is sent.
/// </summary>
internal static class ZipExport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Every column of a telemetry row, in declaration order, without the car it belongs to.</summary>
    public static readonly IReadOnlyList<PropertyInfo> TelemetryColumns =
        [.. typeof(TelemetrySnapshot).GetProperties()
            .Where(p => p.Name != nameof(TelemetrySnapshot.Vehicle)
                && p.GetCustomAttribute<NotMappedAttribute>() is null
                && IsScalar(p.PropertyType))];

    /// <summary>The version this build was made as, with its commit.</summary>
    public static string? AppVersion =>
        typeof(ZipExport).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    /// <summary>
    /// A zip download built by <paramref name="write"/>. Built in a temporary file rather than in
    /// memory or straight into the response: the telemetry alone can run to hundreds of megabytes,
    /// and the zip writer writes synchronously, which the response does not take. The file goes
    /// when the response is sent.
    /// </summary>
    public static async Task<IResult> DownloadAsync(Func<Stream, Task> write, string fileName)
    {
        var file = new FileStream(
            Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await write(file);
            file.Position = 0;
            return Results.File(file, "application/zip", fileName);
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    public static async Task WriteJsonAsync<T>(ZipArchive zip, string name, T value, CancellationToken ct)
    {
        await using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        await JsonSerializer.SerializeAsync(entry, value, Json, ct);
    }

    /// <summary>The rows as a CSV entry: a header of the column names, then a line per row.</summary>
    public static async Task WriteCsvAsync<T>(
        ZipArchive zip, string name, IReadOnlyList<PropertyInfo> columns, IAsyncEnumerable<T> rows, CancellationToken ct)
    {
        await using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        await using var csv = new StreamWriter(entry, Utf8);
        await csv.WriteLineAsync(string.Join(',', columns.Select(c => c.Name)));
        await foreach (var row in rows.WithCancellation(ct))
            await csv.WriteLineAsync(string.Join(',', columns.Select(c => Cell(c.GetValue(row)))));
    }

    public static async Task WriteTextAsync(ZipArchive zip, string name, IEnumerable<string> lines, CancellationToken ct)
    {
        await using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        await using var text = new StreamWriter(entry, Utf8);
        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();
            await text.WriteLineAsync(line);
        }
    }

    // Numbers and times as any spreadsheet or script reads them, whatever the server's language.
    private static string Cell(object? value) => value switch
    {
        null => "",
        DateTime time => time.ToString("O", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        string text => Quote(text),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => Quote(value.ToString() ?? ""),
    };

    private static string Quote(string text) =>
        text.IndexOfAny([',', '"', '\n', '\r']) < 0 ? text : $"\"{text.Replace("\"", "\"\"")}\"";

    private static bool IsScalar(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime);
    }
}
