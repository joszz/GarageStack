using System.Net;
using System.Text;

namespace GarageStack.Tests;

/// <summary>
/// Stands in for an upstream HTTP service (Nominatim, Valhalla, Overpass, Open Charge Map): answers
/// with canned JSON and records every request, so a test can assert what was asked and how often.
/// </summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _queued;
    private readonly (HttpStatusCode Status, string Body) _otherwise;

    /// <summary>Answers every request with <paramref name="body"/>.</summary>
    public FakeHttpHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _queued = new();
        _otherwise = (status, body);
    }

    /// <summary>Answers with <paramref name="responses"/> in order, then with an empty JSON object.</summary>
    public FakeHttpHandler(params (HttpStatusCode Status, string Body)[] responses)
    {
        _queued = new(responses);
        _otherwise = (HttpStatusCode.OK, "{}");
    }

    public List<string> RequestUris { get; } = [];

    public List<string> RequestBodies { get; } = [];

    public int CallCount => RequestUris.Count;

    public string? LastRequestBody => RequestBodies.LastOrDefault();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        RequestUris.Add(request.RequestUri!.ToString());
        RequestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));

        var (status, body) = _queued.Count > 0 ? _queued.Dequeue() : _otherwise;
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>Hands out the same client whatever name is asked for.</summary>
internal sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public FakeHttpClientFactory(HttpMessageHandler handler)
        : this(new HttpClient(handler))
    {
    }

    public HttpClient CreateClient(string name) => client;
}
