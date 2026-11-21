using System.Security.Cryptography;
using GarageStack.Core.Configuration;
using GarageStack.Core.Models;
using GarageStack.Worker.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageStack.Tests;

// FakeServiceScopeFactory / FakePushSender live in WorkerTestFakes.cs.
//
// Full delivery (dead-subscription pruning, pg_notify payload) needs a working
// AppDbContext + PushSubscriptions from the DI scope, which FakeServiceScopeFactory
// deliberately doesn't provide. These tests cover what's reachable without that: the
// VAPID-not-configured fallback path, which is the only path that completes without
// ever needing a real scope.

public class PushSenderServiceTests
{
    private static PushSenderService BuildService(string? publicKey = null, string? privateKey = null) =>
        new(
            NullLogger<PushSenderService>.Instance,
            new FakeServiceScopeFactory(),
            new VapidOptions { PublicKey = publicKey, PrivateKey = privateKey },
            new FakeHttpClientFactory(new HttpClient()),
            WorkerLocalizer.Notifications());

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

    private static Vehicle Car(int id, string vin, string? model) => new() { Id = id, Vin = vin, Model = model };

    [Fact]
    public void TitleWithCar_OneCar_LeavesTheTitleAlone()
    {
        var title = PushSenderService.TitleWithCar("Engine started", [Car(1, "VIN00000000000001", "MG4")], 1);

        Assert.Equal("Engine started", title);
    }

    [Fact]
    public void TitleWithCar_SeveralCars_PutsTheCarInFront()
    {
        Vehicle[] cars = [Car(1, "VIN00000000000001", "MG HS"), Car(2, "VIN00000000000002", "MG4")];

        Assert.Equal("MG4 · Engine started", PushSenderService.TitleWithCar("Engine started", cars, 2));
    }

    [Fact]
    public void TitleWithCar_SameModelTwice_AddsTheEndOfTheVin()
    {
        Vehicle[] cars = [Car(1, "VIN00000000AAAAAA", "MG4"), Car(2, "VIN00000000BBBBBB", "MG4")];

        Assert.Equal("MG4 (…BBBBBB) · Low tyre", PushSenderService.TitleWithCar("Low tyre", cars, 2));
    }

    [Fact]
    public void TitleWithCar_NoCarOrUnknownCar_LeavesTheTitleAlone()
    {
        Vehicle[] cars = [Car(1, "VIN00000000000001", "MG HS"), Car(2, "VIN00000000000002", "MG4")];

        Assert.Equal("Update", PushSenderService.TitleWithCar("Update", cars, null));
        Assert.Equal("Update", PushSenderService.TitleWithCar("Update", cars, 9));
    }
}
