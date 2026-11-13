using System.Text.Json;

namespace GarageStack.Data.Services;

/// <summary>
/// Reads an upstream service's answer with a ceiling on its size, so a misbehaving or
/// misconfigured upstream (Nominatim, Overpass and Valhalla can be self-hosted, at any URL) cannot
/// make the API or the Worker hold an answer of any size in memory. The size is checked against
/// Content-Length before reading and counted while reading, since an answer need not declare it.
/// </summary>
public static class UpstreamJson
{
    /// <summary>Deserialises the body as <typeparamref name="T"/>, reading at most <paramref name="maxBytes"/>.</summary>
    /// <exception cref="UpstreamResponseTooLargeException">The body is larger than that.</exception>
    public static async Task<T?> ReadAsync<T>(HttpResponseMessage response, long maxBytes, CancellationToken ct)
    {
        await using var body = await OpenAsync(response, maxBytes, ct);
        return await JsonSerializer.DeserializeAsync<T>(body, cancellationToken: ct);
    }

    /// <summary>Parses the body as a document, reading at most <paramref name="maxBytes"/>.</summary>
    /// <exception cref="UpstreamResponseTooLargeException">The body is larger than that.</exception>
    public static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response, long maxBytes, CancellationToken ct)
    {
        await using var body = await OpenAsync(response, maxBytes, ct);
        return await JsonDocument.ParseAsync(body, cancellationToken: ct);
    }

    /// <summary>The whole body as bytes, at most <paramref name="maxBytes"/> of them.</summary>
    /// <exception cref="UpstreamResponseTooLargeException">The body is larger than that.</exception>
    public static async Task<byte[]> ReadBytesAsync(HttpResponseMessage response, long maxBytes, CancellationToken ct)
    {
        await using var body = await OpenAsync(response, maxBytes, ct);
        using var copy = new MemoryStream();
        await body.CopyToAsync(copy, ct);
        return copy.ToArray();
    }

    private static async Task<Stream> OpenAsync(HttpResponseMessage response, long maxBytes, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > maxBytes) throw new UpstreamResponseTooLargeException(maxBytes);
        return new CappedStream(await response.Content.ReadAsStreamAsync(ct), maxBytes);
    }

    // Passes reads through until more than the cap has come by, then throws.
    private sealed class CappedStream(Stream inner, long maxBytes) : Stream
    {
        private long _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private int Count(int read)
        {
            _read += read;
            if (_read > maxBytes) throw new UpstreamResponseTooLargeException(maxBytes);
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

/// <summary>
/// An upstream answer larger than the client accepts. A failed request like any other, so it is an
/// <see cref="HttpRequestException"/>: every client already handles those.
/// </summary>
public sealed class UpstreamResponseTooLargeException(long maxBytes)
    : HttpRequestException($"The answer is larger than the {maxBytes / 1024} KB this client accepts");
