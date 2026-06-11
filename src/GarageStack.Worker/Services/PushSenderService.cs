using System.Text.Json;
using GarageStack.Core.Interfaces;
using GarageStack.Data;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelPushSubscription = GarageStack.Core.Models.PushSubscription;

namespace GarageStack.Worker.Services;

public sealed class PushSenderService : IPushSender, IDisposable
{
    private readonly ILogger<PushSenderService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _httpClient;
    private readonly PushServiceClient? _pushClient;

    public PushSenderService(
        ILogger<PushSenderService> logger,
        IServiceScopeFactory scopeFactory,
        IConfiguration config)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _httpClient = new HttpClient();

        var publicKey = config["Vapid:PublicKey"];
        var privateKey = config["Vapid:PrivateKey"];
        var subject = config["Vapid:Subject"] ?? "mailto:admin@garagestack.local";

        if (!string.IsNullOrWhiteSpace(publicKey) && !string.IsNullOrWhiteSpace(privateKey))
        {
            _pushClient = new PushServiceClient(_httpClient)
            {
                DefaultAuthentication = new VapidAuthentication(publicKey, privateKey) { Subject = subject }
            };
        }
        else
        {
            _logger.LogWarning("VAPID keys not configured - push notifications disabled");
        }
    }

    public bool IsConfigured => _pushClient is not null;

    public async Task SendToAllAsync(string title, string body, CancellationToken ct = default, string? category = null, int? vehicleId = null)
    {
        // Always persist so the in-app bell works regardless of push configuration
        try
        {
            using var recordScope = _scopeFactory.CreateScope();
            var recordDb = recordScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = new GarageStack.Core.Models.AppNotification
            {
                Title = title,
                Body = body,
                CreatedAt = DateTime.UtcNow,
                Category = category,
                VehicleId = vehicleId,
            };
            recordDb.AppNotifications.Add(record);
            await recordDb.SaveChangesAsync(ct);

            // Signal the API process via PostgreSQL so connected browser clients get
            // an immediate notificationReceived push without polling.
            var notifyJson = JsonSerializer.Serialize(new
            {
                id = record.Id,
                title = record.Title,
                body = record.Body,
                createdAt = record.CreatedAt.ToString("O"),
                category = record.Category,
                vehicleId = record.VehicleId,
            });
            await recordDb.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_notify('notification_created', {notifyJson})", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist notification record — push delivery will still proceed");
        }

        if (_pushClient is null) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscriptions = await db.PushSubscriptions.AsNoTracking().ToListAsync(ct);
        if (subscriptions.Count == 0) return;

        var payload = System.Text.Json.JsonSerializer.Serialize(new { title, body, icon = "/icons/icon-192.png", category });
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
                await _pushClient.RequestPushMessageDeliveryAsync(pushSub, message, ct);
            }
            catch (PushServiceClientException ex) when (
                ex.StatusCode == System.Net.HttpStatusCode.Gone ||
                ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                dead.Add(sub);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send push to {Endpoint}", sub.Endpoint);
            }
        }

        if (dead.Count > 0)
        {
            // Re-open a write scope to remove expired subscriptions
            using var writeScope = _scopeFactory.CreateScope();
            var writeDb = writeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deadEndpoints = dead.Select(d => d.Endpoint).ToHashSet();
            var toRemove = await writeDb.PushSubscriptions
                .Where(s => deadEndpoints.Contains(s.Endpoint))
                .ToListAsync(ct);
            writeDb.PushSubscriptions.RemoveRange(toRemove);
            await writeDb.SaveChangesAsync(ct);
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
