using System.Collections.Concurrent;
using System.Text.Json;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Extensions;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using ModelPushSubscription = GarageStack.Core.Models.PushSubscription;
using WebPushSubscription = Lib.Net.Http.WebPush.PushSubscription;

namespace GarageStack.Worker.Services;

public sealed class PushSenderService : IPushSender, IDisposable
{
    private const int MaxConcurrentDeliveries = 10;

    private readonly ILogger<PushSenderService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _httpClient;
    private readonly PushServiceClient? _pushClient;

    public PushSenderService(
        ILogger<PushSenderService> logger,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _httpClient = httpClientFactory.CreateClient(nameof(PushSenderService));

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
        int? unreadCount = null;

        // Always persist so the in-app bell works regardless of push configuration
        try
        {
            using var recordScope = _scopeFactory.CreateScope();
            var recordDb = recordScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = new AppNotification
            {
                Title = title,
                Body = body,
                CreatedAt = DateTime.UtcNow,
                Category = category,
                VehicleId = vehicleId,
            };
            recordDb.AppNotifications.Add(record);
            await recordDb.SaveChangesAsync(ct);

            unreadCount = await recordDb.AppNotifications
                .CountAsync(n => !n.IsArchived && !n.IsDeleted, ct);

            // Signal the API process via PostgreSQL so connected browser clients get
            // an immediate notificationReceived push without polling.
            var payload = NotificationCreatedPayload.From(record, unreadCount);
            await recordDb.Database.NotifyAsync(PgChannels.NotificationCreated, payload.ToJson(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist notification record, push delivery will still proceed");
        }

        if (_pushClient is null) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscriptions = await db.PushSubscriptions.AsNoTracking().ToListAsync(ct);
        if (subscriptions.Count == 0) return;

        // Only re-queried if persistence above failed before it could compute this itself.
        unreadCount ??= await db.AppNotifications
            .CountAsync(n => !n.IsArchived && !n.IsDeleted, ct);
        // The service worker (frontend/src/sw.ts) reads title, body, category and unreadCount;
        // it picks the icon itself from the app's own assets.
        var webPushPayload = JsonSerializer.Serialize(new { title, body, category, unreadCount });
        var message = new PushMessage(webPushPayload) { TimeToLive = 3600 };
        var dead = new ConcurrentBag<ModelPushSubscription>();

        // Deliver concurrently (bounded) so one slow/unreachable subscriber can't delay
        // delivery to everyone else; a single push endpoint request can take several
        // seconds if that push service is degraded.
        using var throttle = new SemaphoreSlim(MaxConcurrentDeliveries);
        await Task.WhenAll(subscriptions.Select(async sub =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                var pushSub = new WebPushSubscription();
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
            finally
            {
                throttle.Release();
            }
        }));

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
