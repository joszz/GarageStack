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
    public const int MaxResponseBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task<IReadOnlyList<PhoneFix>> GetPointsAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var fixes = new List<PhoneFix>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var query = $"points?start_at={Utc(from)}&end_at={Utc(to)}&order=asc&per_page={PageSize}&page={page}";
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
                fixes.Add(new(at, latitude, longitude, row.Accuracy,
                    row.Velocity is >= 0 and <= 400 ? row.Velocity : null, tracker));
            }

            if (!response.Headers.TryGetValues("X-Total-Pages", out var headers) ||
                headers.ToArray() is not [var total] ||
                !int.TryParse(total, CultureInfo.InvariantCulture, out var pages) || pages < 0)
                throw new DawarichException("Dawarich omitted valid pagination headers.", retryable: false);
            if (pages > MaxPages)
                throw new DawarichException("Dawarich point window exceeds the page limit.", retryable: false);
            if (page >= pages) return fixes;
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
        throw new DawarichException("Dawarich pagination did not finish.", retryable: false);
    }

    public async Task<IReadOnlyList<PhoneTracker>> GetTrackersAsync(DateTime since, CancellationToken ct) =>
        (await GetPointsAsync(since, time.GetUtcNow().UtcDateTime, ct))
        .Where(f => !string.IsNullOrWhiteSpace(f.TrackerId))
        .GroupBy(f => f.TrackerId!, StringComparer.Ordinal)
        .Select(g => new PhoneTracker(g.Key, new DateTimeOffset(g.Max(f => f.RecordedAt))))
        .OrderBy(t => t.TrackerId, StringComparer.Ordinal).ToArray();

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
