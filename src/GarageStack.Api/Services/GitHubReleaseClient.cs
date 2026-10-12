using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GarageStack.Core.Helpers;

namespace GarageStack.Api.Services;

/// <summary>
/// Asks GitHub for GarageStack's latest release. GitHub's "latest" leaves out drafts and
/// pre-releases, so the answer is always a stable version.
/// </summary>
public sealed class GitHubReleaseClient(IHttpClientFactory httpClientFactory, ILogger<GitHubReleaseClient> logger)
{
    public const string HttpClientName = "github";
    public const string Repository = "joszz/GarageStack";
    public static readonly Uri LatestReleaseUri = new($"https://api.github.com/repos/{Repository}/releases/latest");

    /// <summary>The releases page of <paramref name="version"/>, built here rather than taken from GitHub's answer.</summary>
    public static string ReleasePageUrl(ReleaseVersion version) => $"https://github.com/{Repository}/releases/tag/{version.Tag}";

    /// <returns>The latest release, or null when GitHub has none or could not be asked.</returns>
    public async Task<ReleaseVersion?> GetLatestAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct);

            // A repository without a release yet answers 404.
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("Update check: GitHub answered {StatusCode}", (int)response.StatusCode);
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            var tag = json.RootElement.TryGetProperty("tag_name", out var tagName) && tagName.ValueKind == JsonValueKind.String
                ? tagName.GetString()
                : null;

            if (ReleaseVersion.TryParse(tag, out var version)) return version;

            logger.LogInformation("Update check: the latest release has no version tag");
            return null;
        }
        catch (Exception ex) when ((ex is HttpRequestException or JsonException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            // No internet, a blocked host, a timeout: nothing to tell the user, and nothing wrong here.
            logger.LogInformation("Update check: GitHub could not be reached ({Reason})", ex.GetType().Name);
            return null;
        }
    }
}
