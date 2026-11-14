using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GarageStack.Api.Reference;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GarageStack.Tests;

/// <summary>
/// The API reference: the OpenAPI document, and Scalar's page that shows it. Part of
/// <see cref="AuthenticationFlowTests"/>, where every test that starts a host belongs.
/// </summary>
public partial class AuthenticationFlowTests
{
    private const string DocumentPath = "/api/openapi/v1.json";
    private const string ReferencePagePath = "/api/reference/";

    // The document as of this commit, so a change to the API shows in the pull request that makes it.
    private const string CommittedDocument = "documentation/openapi.json";

    // The document is there for whoever is signed in (a token reads it too), and nobody else.
    [Fact]
    public async Task ApiDocument_IsServedToSignedInCallersOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);

        var anonymous = await client.GetAsync(DocumentPath, ct);
        await PasswordLoginAsync(client, ct);
        var document = await client.GetFromJsonAsync<JsonElement>(DocumentPath, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("GarageStack API", document.GetProperty("info").GetProperty("title").GetString());
        Assert.True(document.GetProperty("paths").TryGetProperty("/api/vehicles/{vin}/trips/{id}/merge-next", out _));
    }

    // Switched off, there is no page and no redirect to sign in for it, and the settings are told
    // so; the document stays, for scripts with a token.
    [Fact]
    public async Task ApiReferencePage_SwitchedOff_IsNotServed_ButTheDocumentIs()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory(settings => settings["API_REFERENCE_ENABLED"] = "false");
        using var client = CreateClient(factory);

        using var signedOut = new HttpRequestMessage(HttpMethod.Get, ReferencePagePath);
        signedOut.Headers.Accept.ParseAdd("text/html");
        var beforeSignIn = await client.SendAsync(signedOut, ct);
        await PasswordLoginAsync(client, ct);
        var page = await client.GetAsync(ReferencePagePath, ct);
        var document = await client.GetAsync(DocumentPath, ct);
        var status = await client.GetFromJsonAsync<ApiReferenceStatus>("/api/tokens/reference", ct);

        Assert.NotEqual(HttpStatusCode.Redirect, beforeSignIn.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.False(status!.Enabled);
    }

    // Demo mode, which these hosts run in, serves the page unless told not to.
    [Fact]
    public async Task ApiReferencePage_IsServedInDemoMode_AndTheSettingsAreToldSo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        await PasswordLoginAsync(client, ct);

        var status = await client.GetFromJsonAsync<ApiReferenceStatus>("/api/tokens/reference", ct);

        Assert.True(status!.Enabled);
    }

    // A browser that is not signed in goes to the login page, which brings it back; a script gets
    // the 401 every other endpoint answers.
    [Fact]
    public async Task ApiReferencePage_SendsABrowserThatIsNotSignedInToTheLoginPage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);

        using var navigation = new HttpRequestMessage(HttpMethod.Get, ReferencePagePath);
        navigation.Headers.Accept.ParseAdd("text/html");
        var browser = await client.SendAsync(navigation, ct);
        var script = await client.GetAsync(ReferencePagePath, ct);

        Assert.Equal(HttpStatusCode.Redirect, browser.StatusCode);
        Assert.Equal("/login?redirect=%2Fapi%2Freference%2F", browser.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, script.StatusCode);
    }

    // Scalar starts from inline scripts, so the page's policy allows scripts by a nonce that is new
    // on every request, and every script tag on the page carries it.
    [Fact]
    public async Task ApiReferencePage_AllowsOnlyItsOwnScripts_ByANonceForEachRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        await PasswordLoginAsync(client, ct);

        var first = await ReferencePageNonceAsync(client, ct);
        var second = await ReferencePageNonceAsync(client, ct);

        Assert.NotEqual(first, second);
    }

    private static async Task<string> ReferencePageNonceAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync(ReferencePagePath, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var policy = string.Join(' ', response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("connect-src 'self';", policy, StringComparison.Ordinal);
        var nonce = Regex.Match(policy, "script-src 'self' 'nonce-([^']+)'").Groups[1].Value;
        Assert.NotEmpty(nonce);

        var page = await response.Content.ReadAsStringAsync(ct);
        var scripts = Regex.Matches(page, "<script[^>]*>").Select(m => m.Value).ToList();
        Assert.NotEmpty(scripts);
        Assert.All(scripts, tag => Assert.Contains($"nonce=\"{nonce}\"", tag, StringComparison.Ordinal));
        return nonce;
    }

    // An endpoint without its answers declared shows in the reference as an empty "200 OK".
    [Fact]
    public async Task EveryEndpoint_DeclaresWhatItAnswers_AndItsGroupIsDescribed()
    {
        await using var factory = CreatePasswordOnlyFactory();
        _ = factory.Server;

        var described = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IHttpMethodMetadata>() is not null
                && e.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>() is not { ExcludeFromDescription: true }
                && e.RoutePattern.RawText!.StartsWith("/api/", StringComparison.Ordinal)
                && !e.RoutePattern.RawText.StartsWith(ApiReferencePage.Path, StringComparison.Ordinal)
                && e.RoutePattern.RawText != OpenApiSetup.DocumentRoute)
            .ToList();

        Assert.NotEmpty(described);
        Assert.All(described, endpoint =>
        {
            var name = endpoint.DisplayName;
            Assert.True(endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
                .Any(p => p.StatusCode is >= 200 and < 400), $"{name} declares no answer: add .Produces<T>() or .Produces(status)");
            Assert.True(endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>() is not null, $"{name} has no summary");
            var tags = endpoint.Metadata.GetOrderedMetadata<ITagsMetadata>().SelectMany(t => t.Tags).ToList();
            Assert.NotEmpty(tags);
            Assert.All(tags, tag => Assert.True(ApiReferenceText.Tags.ContainsKey(tag), $"{name}: tag '{tag}' has no description in ApiReferenceText"));
        });
    }

    // The document is committed, so a change to what the API takes or answers shows up in review.
    // A mismatch rewrites the committed copy and fails: look at the change, and commit it.
    [Fact]
    public async Task ApiDocument_MatchesTheCommittedCopy()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreatePasswordOnlyFactory();
        using var client = CreateClient(factory);
        await PasswordLoginAsync(client, ct);

        var current = CommittedForm(await client.GetStringAsync(DocumentPath, ct));
        var path = RepositoryFiles.PathOf(CommittedDocument);
        var committed = File.Exists(path) ? (await File.ReadAllTextAsync(path, ct)).Replace("\r\n", "\n", StringComparison.Ordinal) : null;

        if (committed != current)
        {
            await File.WriteAllTextAsync(path, current, ct);
            Assert.Fail($"{CommittedDocument} did not match the API and has been rewritten. Check the change and commit it.");
        }
    }

    // The parts that differ by where and from what the API was built are left out.
    private static string CommittedForm(string document)
    {
        var json = JsonNode.Parse(document)!.AsObject();
        json["info"]!["version"] = "(the release)";
        json.Remove("servers");
        return json.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n";
    }
}
