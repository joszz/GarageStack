using System.Security.Cryptography;
using GarageStack.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

// FakeServiceScopeFactory / FakePushSender live in WorkerTestFakes.cs.
//
// Full delivery (dead-subscription pruning, pg_notify payload) needs a working
// AppDbContext + PushSubscriptions from the DI scope, which FakeServiceScopeFactory
// deliberately doesn't provide. These tests cover what's reachable without that: the
// VAPID-not-configured fallback path, which is the only path that completes without
// ever needing a real scope.

file sealed class NoopHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}

public class PushSenderServiceTests
{
    private static IConfiguration BuildConfig(string? publicKey = null, string? privateKey = null)
    {
        var dict = new Dictionary<string, string?>();
        if (publicKey is not null) dict["Vapid:PublicKey"] = publicKey;
        if (privateKey is not null) dict["Vapid:PrivateKey"] = privateKey;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static PushSenderService BuildService(string? publicKey = null, string? privateKey = null) =>
        new(
            NullLogger<PushSenderService>.Instance,
            new FakeServiceScopeFactory(),
            BuildConfig(publicKey, privateKey),
            new NoopHttpClientFactory());

    // VapidAuthentication validates key shape eagerly (65-byte uncompressed P-256 public
    // key, 32-byte private scalar), so "both keys set" needs a real - if freshly generated
    // and otherwise meaningless - key pair rather than placeholder strings.
    private static (string PublicKey, string PrivateKey) GenerateFakeVapidKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(true);
        var publicKeyBytes = new byte[65];
        publicKeyBytes[0] = 0x04;
        Buffer.BlockCopy(p.Q.X!, 0, publicKeyBytes, 1, 32);
        Buffer.BlockCopy(p.Q.Y!, 0, publicKeyBytes, 33, 32);
        return (Base64UrlEncode(publicKeyBytes), Base64UrlEncode(p.D!));
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void IsConfigured_NoVapidKeys_ReturnsFalse()
    {
        using var svc = BuildService();

        Assert.False(svc.IsConfigured);
    }

    [Fact]
    public void IsConfigured_OnlyPublicKeySet_ReturnsFalse()
    {
        using var svc = BuildService(publicKey: "some-public-key");

        Assert.False(svc.IsConfigured);
    }

    [Fact]
    public void IsConfigured_BothKeysSet_ReturnsTrue()
    {
        var (publicKey, privateKey) = GenerateFakeVapidKeyPair();
        using var svc = BuildService(publicKey, privateKey);

        Assert.True(svc.IsConfigured);
    }

    [Fact]
    public async Task SendToAllAsync_NoVapidKeys_CompletesWithoutThrowing()
    {
        using var svc = BuildService();

        // The record-persist step also fails silently (FakeServiceScopeFactory resolves no
        // real AppDbContext), and push delivery is skipped entirely since IsConfigured is
        // false - neither failure should propagate out of SendToAllAsync.
        await svc.SendToAllAsync("Test", "Body", CancellationToken.None);
    }
}
