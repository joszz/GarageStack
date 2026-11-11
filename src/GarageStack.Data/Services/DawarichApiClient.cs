using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using GarageStack.Core.Configuration;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;

namespace GarageStack.Data.Services;

public sealed class DawarichApiClient(HttpClient client, DawarichOptions options, TimeProvider time) : IDawarichClient
{
    public const int MaxPages = 100;
    public const int PageSize = 1000;
    /// <summary>
    /// Tracker discovery reads only the newest points. A phone that reports every few seconds fills
    /// a month with far more points than it takes to see which trackers are in use, and a tracker
    /// missing from them can still be typed in by its ID.
    /// </summary>
    public const int DiscoveryPages = 5;
    public const int MaxResponseBytes = 4 * 1024 * 1024;
    /// <summary>Speeds above this are a GPS glitch, not a car.</summary>
    public const double MaxSpeedKmh = 400;
    // Dawarich stores velocity in metres per second whatever the app sent: it converts OwnTracks'
    // km/h itself, and Overland and its own apps already report m/s.
    private const double KmhPerMetrePerSecond = 3.6;
    private static readonly TimeSpan PagePause = TimeSpan.FromMilliseconds(250);
    private static readonly JsonSerializerOptions Json = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task<IReadOnlyList<PhoneFix>> GetPointsAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var fixes = new List<PhoneFix>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var pages = await ReadPageAsync(from, to, "asc", page, fixes, ct);
            if (pages > MaxPages)
                throw new DawarichException("Dawarich point window exceeds the page limit.", retryable: false);
            if (page >= pages) return fixes;
            await Task.Delay(PagePause, ct);
        }
        throw new DawarichException("Dawarich pagination did not finish.", retryable: false);
    }

    public async Task<IReadOnlyList<PhoneTracker>> GetTrackersAsync(DateTime since, CancellationToken ct)
    {
        var fixes = new List<PhoneFix>();
        var until = time.GetUtcNow().UtcDateTime;
        for (var page = 1; page <= DiscoveryPages; page++)
        {
            if (page >= await ReadPageAsync(since, until, "desc", page, fixes, ct)) break;
            if (page < DiscoveryPages) await Task.Delay(PagePause, ct);
        }
        return fixes
            .Where(f => !string.IsNullOrWhiteSpace(f.TrackerId))
            .GroupBy(f => f.TrackerId!, StringComparer.Ordinal)
            .Select(g => new PhoneTracker(g.Key, new DateTimeOffset(g.Max(f => f.RecordedAt))))
            .OrderBy(t => t.TrackerId, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Adds one page of points to <paramref name="fixes"/> and returns the total page count.</summary>
    private async Task<int> ReadPageAsync(
        DateTime from, DateTime to, string order, int page, List<PhoneFix> fixes, CancellationToken ct)
    {
        var query = $"points?start_at={Utc(from)}&end_at={Utc(to)}&order={order}&per_page={PageSize}&page={page}";
        using var response = await SendAsync(query, ct);
        var bytes = await ReadAsync(response, ct);
        List<PointRow> rows;
        try
        {
            rows = JsonSerializer.Deserialize<List<PointRow>>(bytes, Json)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new DawarichException("Dawarich returned invalid point data.", retryable: false);
        }

        if (rows.Count > PageSize)
            throw new DawarichException("Dawarich exceeded the requested page size.", retryable: false);
        foreach (var row in rows)
        {
            if (row is null || row.Timestamp is not { } timestamp || timestamp < 0 || timestamp > 253402300799 ||
                row.Latitude is not { } latitude || row.Longitude is not { } longitude ||
                !double.IsFinite(latitude) || latitude is < -90 or > 90 ||
                !double.IsFinite(longitude) || longitude is < -180 or > 180)
                throw new DawarichException("Dawarich returned invalid coordinates or a non-second timestamp.", retryable: false);
            var at = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
            if (at < from || at > to) continue;
            var tracker = string.IsNullOrWhiteSpace(row.TrackerId) ? row.Topic : row.TrackerId;
            if (tracker?.Length > VehiclePhoneTracker.MaxTrackerLength) tracker = null;
            var speed = row.Velocity * KmhPerMetrePerSecond;
            fixes.Add(new(at, latitude, longitude, row.Accuracy,
                speed is >= 0 and <= MaxSpeedKmh ? speed : null, tracker));
        }

        if (!response.Headers.TryGetValues("X-Total-Pages", out var headers) ||
            headers.ToArray() is not [var total] ||
            !int.TryParse(total, CultureInfo.InvariantCulture, out var pages) || pages < 0)
            throw new DawarichException("Dawarich omitted valid pagination headers.", retryable: false);
        return pages;
    }

    public async Task<DawarichHealth> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var response = await SendAsync("settings", ct);
            var bytes = await ReadAsync(response, ct);
            using var data = JsonDocument.Parse(bytes);
            return data.RootElement.ValueKind == JsonValueKind.Object && data.RootElement.TryGetProperty("settings", out _)
                ? DawarichHealth.Ok : DawarichHealth.Unreachable;
        }
        catch (DawarichException ex) { return ex.BadKey ? DawarichHealth.BadKey : DawarichHealth.Unreachable; }
        catch (JsonException) { return DawarichHealth.Unreachable; }
    }

    private async Task<HttpResponseMessage> SendAsync(string path, CancellationToken ct)
    {
        if (!options.Enabled) throw new DawarichException("Dawarich is not configured.", retryable: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{options.Url}/api/v1/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.OK) return response;
            var status = response.StatusCode;
            response.Dispose();
            throw new DawarichException($"Dawarich returned HTTP {(int)status}.",
                badKey: status == HttpStatusCode.Unauthorized,
                retryable: status == HttpStatusCode.TooManyRequests || (int)status >= 500);
        }
        catch (HttpRequestException) { throw new DawarichException("Dawarich could not be reached."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new DawarichException("Dawarich did not answer in time."); }
    }

    private static async Task<byte[]> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new DawarichException("Dawarich response exceeds the size limit.", retryable: false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var result = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (result.Length + count > MaxResponseBytes)
                    throw new DawarichException("Dawarich response exceeds the size limit.", retryable: false);
                result.Write(buffer, 0, count);
            }
            return result.ToArray();
        }
        catch (IOException) { throw new DawarichException("Dawarich response could not be read."); }
        catch (HttpRequestException) { throw new DawarichException("Dawarich response could not be read."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new DawarichException("Dawarich response timed out."); }
    }

    private static string Utc(DateTime at) =>
        Uri.EscapeDataString(at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

    private sealed record PointRow(
        [property: JsonPropertyName("timestamp")] long? Timestamp,
        [property: JsonPropertyName("latitude")] double? Latitude,
        [property: JsonPropertyName("longitude")] double? Longitude,
        [property: JsonPropertyName("accuracy")] double? Accuracy,
        [property: JsonPropertyName("velocity")] double? Velocity,
        [property: JsonPropertyName("tracker_id")] string? TrackerId,
        [property: JsonPropertyName("topic")] string? Topic);
}
