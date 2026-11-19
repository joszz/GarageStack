using System.Collections.Concurrent;
using GarageStack.Core.Configuration;
using GarageStack.Core.Helpers;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using GarageStack.Data;
using GarageStack.Data.Extensions;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Localization;
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
    private readonly TimeProvider _clock;
    private readonly IStringLocalizer<NotificationStrings> _strings;

    public PushSenderService(
        ILogger<PushSenderService> logger,
        IServiceScopeFactory scopeFactory,
        VapidOptions vapid,
        IHttpClientFactory httpClientFactory,
        IStringLocalizer<NotificationStrings> strings,
        TimeProvider? clock = null)
    {
        _logger = logger;
        _strings = strings;
        _clock = clock ?? TimeProvider.System;
        _scopeFactory = scopeFactory;
        _httpClient = httpClientFactory.CreateClient(nameof(PushSenderService));

        if (vapid.IsConfigured)
        {
            _pushClient = new PushServiceClient(_httpClient)
            {
                DefaultAuthentication = new VapidAuthentication(vapid.PublicKey, vapid.PrivateKey) { Subject = vapid.Subject }
            };
        }
        else
        {
            _logger.LogWarning("VAPID keys not configured - push notifications disabled");
        }
    }

    public bool IsConfigured => _pushClient is not null;

    /// <summary>
    /// The push title with the car in front ("MG4 · Engine started") when the account has more
    /// than one car, named as the app names it (frontend/src/utils/vehicleLabels.ts): its model,
    /// with the end of the VIN when another car shares that model. Unchanged with one car.
    /// </summary>
    internal static string TitleWithCar(string title, IReadOnlyList<Vehicle> vehicles, int? vehicleId)
    {
        if (vehicles.Count < 2 || vehicles.FirstOrDefault(v => v.Id == vehicleId) is not { } car) return title;
        static string Name(Vehicle v) => v.Model ?? v.Series ?? v.Vin;
        var name = Name(car);
        var shared = name != car.Vin && vehicles.Count(v => Name(v) == name) > 1;
        var label = shared ? $"{name} (…{car.Vin[^Math.Min(6, car.Vin.Length)..]})" : name;
        return $"{label} · {title}";
    }

    public async Task SendToAllAsync(string title, string body, CancellationToken ct = default, string? category = null, int? vehicleId = null)
    {
        int? unreadCount = null;

        // Always persist so the in-app bell works regardless of push configuration
        try
        {
            using var recordScope = _scopeFactory.CreateScope();
            var notifications = recordScope.ServiceProvider.GetRequiredService<INotificationRepository>();
            var record = new AppNotification
            {
                Title = title,
                Body = body,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
                Category = category,
                VehicleId = vehicleId,
            };
            await notifications.AddAsync(record, ct);
            unreadCount = await notifications.CountUnreadAsync(ct);

            // Signal the API process via PostgreSQL so connected browser clients get
            // an immediate notificationReceived push without polling.
            var payload = NotificationCreatedPayload.From(record, unreadCount);
            var db = recordScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.NotifyAsync(PgChannels.NotificationCreated, payload.ToJson(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist notification record, push delivery will still proceed");
        }

        if (_pushClient is null) return;

        using var scope = _scopeFactory.CreateScope();
        var subscriptionRepository = scope.ServiceProvider.GetRequiredService<IPushSubscriptionRepository>();
        // Each device switched its own types off. The in-app record above is shared and kept
        // regardless; only the push to a device that does not want this type is left out.
        var subscriptions = (await subscriptionRepository.ListAsync(ct))
            .Where(s => NotificationCategories.IsWanted(category, s.ExcludedCategories))
            .ToList();
        if (subscriptions.Count == 0) return;

        // Only re-queried if persistence above failed before it could compute this itself.
        unreadCount ??= await scope.ServiceProvider.GetRequiredService<INotificationRepository>().CountUnreadAsync(ct);
        // The service worker (frontend/src/sw.ts) shows it; it picks the icon itself from the
        // app's own assets. Buttons on the notification need the car they act on, and with more
        // than one car on the account the title says which car it is about.
        var vehicles = vehicleId is null
            ? []
            : await scope.ServiceProvider.GetRequiredService<IVehicleRepository>().GetAllAsync(ct);
        var vin = NotificationActions.For(category).Count > 0
            ? vehicles.FirstOrDefault(v => v.Id == vehicleId)?.Vin
            : null;
        var pushTitle = TitleWithCar(title, vehicles, vehicleId);
        var webPushPayload = PushPayload.Serialize(PushPayload.For(pushTitle, body, category, unreadCount, vin, _strings));
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
                    _logger.LogWarning(ex, "Failed to send push through {PushService}", LogRedaction.UrlHost(sub.Endpoint));
                }
            }
            finally
            {
                throttle.Release();
            }
        }));

        // The push service said these subscriptions are gone for good.
        if (dead.Count > 0)
            await subscriptionRepository.RemoveAsync([.. dead.Select(d => d.Endpoint).Distinct()], ct);
    }

    public void Dispose() => _httpClient.Dispose();
}
