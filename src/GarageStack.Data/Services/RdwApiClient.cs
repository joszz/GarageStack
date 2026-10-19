using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.Extensions.Logging;

namespace GarageStack.Data.Services;

/// <summary>
/// The RDW's open data (Socrata JSON on opendata.rdw.nl), asked by plate: the registration, its
/// recall statuses, and the details of recalls not seen before. Nothing but the plate, and the
/// reference codes the RDW itself handed out, is sent. Switched off, it asks nothing at all.
/// </summary>
public sealed class RdwApiClient(
    IHttpClientFactory httpClientFactory,
    RdwOptions options,
    ILogger<RdwApiClient> logger) : IRdwClient
{
    public const string HttpClientName = "rdw";

    internal const string RegistrationDataset = "m9d7-ebf2";
    internal const string RecallStatusDataset = "t49b-isb7";
    internal const string RecallDetailsDataset = "j9yg-7rg9";

    // One gate for all three datasets: they share a host and a rate limit. The API (right after a
    // plate is entered) and the Worker each have their own client, so this keeps one process polite.
    private readonly UpstreamRateGate _gate = new();
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RetryAfterThrottle = TimeSpan.FromMinutes(5);
    // A browser waits on the API's lookup; the Worker tries again on its next round.
    private static readonly TimeSpan WaitAtMost = TimeSpan.FromSeconds(20);

    public async Task<RdwRegistration?> GetRegistrationAsync(string plate, CancellationToken ct = default)
    {
        var rows = await GetAsync<RegistrationRow>(RegistrationDataset, $"kenteken={Uri.EscapeDataString(plate)}", ct);

        // Socrata answers a plate it does not know with an empty array, not a 404.
        if (rows.Count == 0) return null;

        var row = rows[0];
        return new RdwRegistration(
            Text(row.Make, RdwLimits.NameMaxLength),
            Text(row.Model, RdwLimits.NameMaxLength),
            ParseDate(row.ApkExpiresOn),
            ParseYesNo(row.WamInsured),
            ParseYesNo(row.OpenRecallIndicator));
    }

    public async Task<IReadOnlyList<RdwRecallStatus>> GetRecallStatusesAsync(string plate, CancellationToken ct = default)
    {
        var rows = await GetAsync<RecallStatusRow>(RecallStatusDataset, $"kenteken={Uri.EscapeDataString(plate)}", ct);
        return rows
            .Where(r => IsCode(r.Code) && !string.IsNullOrWhiteSpace(r.Status))
            .Select(r => new RdwRecallStatus(r.Code!, r.Status!.Trim()))
            .ToList();
    }

    public async Task<IReadOnlyList<RdwRecallDetails>> GetRecallDetailsAsync(
        IReadOnlyCollection<string> codes, CancellationToken ct = default)
    {
        // The codes go into a SoQL filter, so only ones shaped like the RDW's own make it there.
        var wanted = codes.Where(IsCode).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0) return [];

        var filter = $"referentiecode_rdw in({string.Join(',', wanted.Select(c => $"'{c}'"))})";
        var rows = await GetAsync<RecallDetailsRow>(RecallDetailsDataset, $"$where={Uri.EscapeDataString(filter)}", ct);
        return rows
            .Where(r => IsCode(r.Code))
            .Select(r => new RdwRecallDetails(
                r.Code!,
                Text(r.Defect, RdwLimits.TextMaxLength),
                Text(r.Risk, RdwLimits.TextMaxLength),
                Text(r.Repair, RdwLimits.TextMaxLength),
                Text(r.Severity, RdwLimits.SeverityMaxLength)))
            .ToList();
    }

    private async Task<IReadOnlyList<T>> GetAsync<T>(string dataset, string query, CancellationToken ct)
    {
        if (!options.Enabled)
            throw new RdwUnavailableException("RDW lookups are switched off");

        using var entry = await _gate.TryEnterAsync(WaitAtMost, failWhenBackingOff: true, ct);
        if (entry.Refusal == GateRefusal.BackingOff)
            throw new RdwUnavailableException("The RDW asked to slow down; trying again later");
        if (!entry.Entered)
            throw new RdwUnavailableException("Another RDW lookup is still running");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{RdwOptions.BaseUrl}/resource/{dataset}.json?{query}");
        if (options.AppToken is { } token)
            request.Headers.Add("X-App-Token", token);

        var client = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using var answer = await _gate.SendAsync(
                token => client.SendAsync(request, token), MinInterval, honourBackoff: false, RetryAfterThrottle, ct);
            var response = answer.Response;

            if (answer.Backoff is { } backoff)
            {
                logger.LogWarning("RDW rate-limited ({Status}); pausing lookups for {Seconds}s",
                    (int)response.StatusCode, (int)backoff.TotalSeconds);
                throw new RdwUnavailableException("The RDW asked to slow down; trying again later");
            }

            if (response.StatusCode != HttpStatusCode.OK)
                throw new RdwUnavailableException($"The RDW answered {(int)response.StatusCode}");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<List<T>>(stream, cancellationToken: ct) ?? [];
        }
        catch (HttpRequestException ex)
        {
            throw new RdwUnavailableException("The RDW could not be reached", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new RdwUnavailableException("The RDW did not answer in time", ex);
        }
        catch (JsonException ex)
        {
            throw new RdwUnavailableException("The RDW answered with something other than data", ex);
        }
    }

    // The RDW's recall reference codes, such as MGP230061.
    private static bool IsCode(string? code) =>
        code is { Length: > 0 and <= 32 } && code.All(char.IsAsciiLetterOrDigit);

    private static string? Text(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Truncate(maxLength);

    // Dates come as yyyyMMdd text ("20280426"), a day on the Dutch calendar.
    internal static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    // "Ja" or "Nee"; anything else ("N.v.t.", missing) says nothing either way.
    internal static bool? ParseYesNo(string? value) => value?.Trim() switch
    {
        "Ja" => true,
        "Nee" => false,
        _ => null,
    };

    // ---------------------------------------------------------------------------
    // Socrata rows: every value is text, whatever the column's type.
    // ---------------------------------------------------------------------------

    private sealed class RegistrationRow
    {
        [JsonPropertyName("merk")] public string? Make { get; init; }
        [JsonPropertyName("handelsbenaming")] public string? Model { get; init; }
        [JsonPropertyName("vervaldatum_apk")] public string? ApkExpiresOn { get; init; }
        [JsonPropertyName("wam_verzekerd")] public string? WamInsured { get; init; }
        [JsonPropertyName("openstaande_terugroepactie_indicator")] public string? OpenRecallIndicator { get; init; }
    }

    private sealed class RecallStatusRow
    {
        [JsonPropertyName("referentiecode_rdw")] public string? Code { get; init; }
        [JsonPropertyName("code_status")] public string? Status { get; init; }
    }

    private sealed class RecallDetailsRow
    {
        [JsonPropertyName("referentiecode_rdw")] public string? Code { get; init; }
        [JsonPropertyName("omschrijving_defect")] public string? Defect { get; init; }
        // "materiële gevolgen", with the ë dropped from the column name.
        [JsonPropertyName("materi_le_gevolgen")] public string? Risk { get; init; }
        [JsonPropertyName("beschrijving_van_het_herstel")] public string? Repair { get; init; }
        // ERN (serious), GEM (moderate) or NNB (not yet known).
        [JsonPropertyName("risicobeoordeling_rdw")] public string? Severity { get; init; }
    }
}
