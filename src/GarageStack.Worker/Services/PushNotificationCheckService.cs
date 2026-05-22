using GarageStack.Core.Interfaces;
using GarageStack.Data;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using ModelPushSubscription = GarageStack.Core.Models.PushSubscription;

namespace GarageStack.Worker.Services;

public class PushNotificationCheckService(
    ILogger<PushNotificationCheckService> logger,
    IServiceScopeFactory scopeFactory,
    IConfiguration config) : BackgroundService
{
    private readonly Dictionary<string, DateTime> _lastNotified = new();
    private readonly TimeSpan _cooldown = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var publicKey = config["Vapid:PublicKey"];
        var privateKey = config["Vapid:PrivateKey"];
        var subject = config["Vapid:Subject"] ?? "mailto:admin@garagestack.local";

        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
        {
            logger.LogWarning("VAPID keys not configured — push notifications disabled. Set Vapid:PublicKey and Vapid:PrivateKey in environment");
            return;
        }

        // PushServiceClient is expensive — create once and reuse
        using var httpClient = new HttpClient();
        var pushClient = new PushServiceClient(httpClient)
        {
            DefaultAuthentication = new VapidAuthentication(publicKey, privateKey) { Subject = subject }
        };

        logger.LogInformation("Push notification service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            try
            {
                await CheckAndNotifyAsync(pushClient, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error during push notification check");
            }
        }
    }

    private async Task CheckAndNotifyAsync(PushServiceClient pushClient, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var vehicles = await db.Vehicles.ToListAsync(ct);
        var subscriptions = await db.PushSubscriptions.ToListAsync(ct);
        if (subscriptions.Count == 0) return;

        foreach (var vehicle in vehicles)
        {
            var snapshot = await telemetry.GetMergedLatestAsync(vehicle.Id, ct);
            if (snapshot is null) continue;

            var alerts = new List<(string key, string title, string body)>();
            CheckTyrePressure(snapshot, alerts);
            CheckEvSoc(snapshot, alerts);

            foreach (var (key, title, body) in alerts)
            {
                var notifKey = $"{vehicle.Vin}/{key}";
                if (_lastNotified.TryGetValue(notifKey, out var last) && DateTime.UtcNow - last < _cooldown)
                    continue;

                _lastNotified[notifKey] = DateTime.UtcNow;
                await SendToAllAsync(pushClient, subscriptions, title, body, db, ct);
                logger.LogInformation("Push sent: {Key} — {Title}", notifKey, title);
            }
        }
    }

    private static void CheckTyrePressure(Core.Models.TelemetrySnapshot s, List<(string, string, string)> alerts)
    {
        var low = new List<string>();
        if (s.TyrePressureFrontLeft is not null && s.TyrePressureFrontLeft < 2.2) low.Add("FL");
        if (s.TyrePressureFrontRight is not null && s.TyrePressureFrontRight < 2.2) low.Add("FR");
        if (s.TyrePressureRearLeft is not null && s.TyrePressureRearLeft < 2.2) low.Add("RL");
        if (s.TyrePressureRearRight is not null && s.TyrePressureRearRight < 2.2) low.Add("RR");

        if (low.Count > 0)
            alerts.Add(("low-tyre", "Low Tyre Pressure", $"Tyre pressure low: {string.Join(", ", low)}"));
    }

    private static void CheckEvSoc(Core.Models.TelemetrySnapshot s, List<(string, string, string)> alerts)
    {
        if (s.EvSocPercent is not null && s.EvSocPercent < 20)
            alerts.Add(("low-ev", "Low EV Battery", $"EV battery at {s.EvSocPercent:F0}%"));
    }

    private async Task SendToAllAsync(
        PushServiceClient pushClient,
        List<ModelPushSubscription> subscriptions,
        string title, string body,
        AppDbContext db,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { title, body, icon = "/icons/icon-192.png" });
        var message = new PushMessage(payload) { TimeToLive = 3600 };
        var dead = new List<ModelPushSubscription>();

        foreach (var sub in subscriptions)
        {
            var pushSub = new PushSubscription();
            pushSub.Endpoint = sub.Endpoint;
            pushSub.SetKey(PushEncryptionKeyName.P256DH, sub.P256DhKey);
            pushSub.SetKey(PushEncryptionKeyName.Auth, sub.AuthKey);

            try
            {
                await pushClient.RequestPushMessageDeliveryAsync(pushSub, message, ct);
            }
            catch (PushServiceClientException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                dead.Add(sub);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send push to {Endpoint}", sub.Endpoint);
            }
        }

        if (dead.Count > 0)
        {
            db.PushSubscriptions.RemoveRange(dead);
            await db.SaveChangesAsync(ct);
        }
    }
}
