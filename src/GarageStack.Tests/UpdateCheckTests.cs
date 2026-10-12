using System.Net;
using GarageStack.Api.Services;
using GarageStack.Core.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

public class ReleaseVersionTests
{
    private static ReleaseVersion V(string text)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version), text);
        return version;
    }

    [Theory]
    [InlineData("v0.9.2", 0, 9, 2, false)]
    [InlineData("0.9.2", 0, 9, 2, false)]
    // MinVer's stamp on a build made after 0.9.2, with the commit as build metadata.
    [InlineData("0.9.3-preview.0.4+1a2b3c4", 0, 9, 3, true)]
    [InlineData("1.10.0+abc", 1, 10, 0, false)]
    public void TryParse_ReadsReleaseTagsAndMinVerStamps(string text, int major, int minor, int patch, bool preRelease)
    {
        Assert.Equal(new ReleaseVersion(major, minor, patch, preRelease), V(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.2")]
    [InlineData("v1.2.3.4")]
    public void TryParse_RejectsWhatIsNoVersion(string? text)
    {
        Assert.False(ReleaseVersion.TryParse(text, out _));
    }

    // A build from source without APP_VERSION; every release would look newer.
    [Fact]
    public void LocalBuild_IsNotAKnownVersion()
    {
        Assert.False(V("0.0.0-local").IsKnown);
        Assert.True(V("0.9.2").IsKnown);
    }

    [Theory]
    [InlineData("0.9.2", "0.9.3", true)]
    [InlineData("0.9.2", "0.9.2", false)]
    [InlineData("0.9.3", "0.9.2", false)]
    // Built from main after 0.9.2: already past it, but not yet 0.9.3.
    [InlineData("0.9.3-preview.0.4", "0.9.2", false)]
    [InlineData("0.9.3-preview.0.4", "0.9.3", true)]
    // Numbers, not text: 0.10.0 is newer than 0.9.9.
    [InlineData("0.9.9", "0.10.0", true)]
    public void IsOlderThan_ComparesLikeReleasesDo(string current, string latest, bool older)
    {
        Assert.Equal(older, V(current).IsOlderThan(V(latest)));
    }
}

public class GitHubReleaseClientTests
{
    private static GitHubReleaseClient Client(HttpMessageHandler handler) =>
        new(new FakeHttpClientFactory(handler), NullLogger<GitHubReleaseClient>.Instance);

    [Fact]
    public async Task GetLatest_ReadsTheReleaseTag()
    {
        var handler = new FakeHttpHandler("""{ "tag_name": "v0.9.3", "html_url": "https://example.com/elsewhere" }""");

        var latest = await Client(handler).GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new ReleaseVersion(0, 9, 3, false), latest);
        Assert.Equal(GitHubReleaseClient.LatestReleaseUri.ToString(), Assert.Single(handler.RequestUris));
    }

    [Theory]
    // No release yet.
    [InlineData(HttpStatusCode.NotFound, "{}")]
    // Rate limited.
    [InlineData(HttpStatusCode.Forbidden, "{}")]
    [InlineData(HttpStatusCode.OK, """{ "tag_name": "nightly" }""")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task GetLatest_AnythingButAReleaseTag_IsNoAnswer(HttpStatusCode status, string body)
    {
        var latest = await Client(new FakeHttpHandler(body, status)).GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Null(latest);
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("No such host is known.");
    }

    [Fact]
    public async Task GetLatest_GitHubUnreachable_IsNoAnswer()
    {
        Assert.Null(await Client(new UnreachableHandler()).GetLatestAsync(TestContext.Current.CancellationToken));
    }

    // The link the footer opens is built from the version, never taken from GitHub's answer.
    [Fact]
    public void ReleasePageUrl_PointsAtTheReleaseOnGitHub()
    {
        Assert.Equal(
            "https://github.com/joszz/GarageStack/releases/tag/v0.9.3",
            GitHubReleaseClient.ReleasePageUrl(new ReleaseVersion(0, 9, 3, false)));
    }
}

public class UpdateCheckServiceTests
{
    private static readonly ReleaseVersion Running = new(0, 9, 2, false);

    private static UpdateCheckService Service(params (HttpStatusCode, string)[] answers) =>
        new(
            new GitHubReleaseClient(new FakeHttpClientFactory(new FakeHttpHandler(answers)), NullLogger<GitHubReleaseClient>.Instance),
            new UpdateCheckOptions(true, Running),
            NullLogger<UpdateCheckService>.Instance);

    [Fact]
    public async Task Check_NewerRelease_OffersItWithItsReleasePage()
    {
        var service = Service((HttpStatusCode.OK, """{ "tag_name": "v0.9.3" }"""));

        Assert.True(await service.CheckAsync(Running, TestContext.Current.CancellationToken));

        Assert.Equal(
            new UpdateInfo(true, "0.9.3", "https://github.com/joszz/GarageStack/releases/tag/v0.9.3"),
            service.Latest);
    }

    [Fact]
    public async Task Check_SameRelease_OffersNothing()
    {
        var service = Service((HttpStatusCode.OK, """{ "tag_name": "v0.9.2" }"""));

        await service.CheckAsync(Running, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateInfo.None, service.Latest);
    }

    // A day without internet should not hide a release the previous check found.
    [Fact]
    public async Task Check_GitHubUnreachable_KeepsThePreviousAnswer()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = Service(
            (HttpStatusCode.OK, """{ "tag_name": "v0.9.3" }"""),
            (HttpStatusCode.ServiceUnavailable, "{}"));

        await service.CheckAsync(Running, ct);
        Assert.False(await service.CheckAsync(Running, ct));

        Assert.True(service.Latest.UpdateAvailable);
    }

    [Theory]
    [InlineData(false, "0.9.2")]
    [InlineData(true, "0.0.0-local")]
    public async Task Run_SwitchedOffOrWithoutAVersion_NeverAsksGitHub(bool enabled, string running)
    {
        var handler = new FakeHttpHandler("""{ "tag_name": "v9.9.9" }""");
        Assert.True(ReleaseVersion.TryParse(running, out var current));
        using var service = new UpdateCheckService(
            new GitHubReleaseClient(new FakeHttpClientFactory(handler), NullLogger<GitHubReleaseClient>.Instance),
            new UpdateCheckOptions(enabled, current),
            NullLogger<UpdateCheckService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.ExecuteTask!;

        Assert.Equal(0, handler.CallCount);
        Assert.Equal(UpdateInfo.None, service.Latest);
    }
}
