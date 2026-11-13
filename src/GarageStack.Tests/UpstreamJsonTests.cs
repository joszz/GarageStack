using System.Text;
using GarageStack.Data.Services;

namespace GarageStack.Tests;

public class UpstreamJsonTests
{
    private sealed record Answer(string Name);

    // A body whose length the response does not declare, as a chunked answer arrives.
    private sealed class Unseekable(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private static HttpResponseMessage Response(string json, bool declareLength) => new()
    {
        Content = declareLength
            ? new StringContent(json, Encoding.UTF8, "application/json")
            : new StreamContent(new Unseekable(Encoding.UTF8.GetBytes(json))),
    };

    private static readonly string Small = """{"Name":"Utrecht"}""";
    private static readonly string Large = $$"""{"Name":"{{new string('x', 4096)}}"}""";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnAnswerWithinTheCap_IsRead(bool declareLength)
    {
        using var response = Response(Small, declareLength);

        var answer = await UpstreamJson.ReadAsync<Answer>(response, 1024, TestContext.Current.CancellationToken);

        Assert.Equal("Utrecht", answer!.Name);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnAnswerOverTheCap_IsRefused_WhetherOrNotItDeclaresItsLength(bool declareLength)
    {
        using var response = Response(Large, declareLength);

        await Assert.ThrowsAsync<UpstreamResponseTooLargeException>(() =>
            UpstreamJson.ReadAsync<Answer>(response, 1024, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheRefusal_IsAFailedRequest_SoEveryClientsExistingHandlingCatchesIt()
    {
        using var response = Response(Large, declareLength: false);

        var refusal = await Assert.ThrowsAnyAsync<HttpRequestException>(() =>
            UpstreamJson.ReadBytesAsync(response, 1024, TestContext.Current.CancellationToken));

        Assert.IsType<UpstreamResponseTooLargeException>(refusal);
    }
}
