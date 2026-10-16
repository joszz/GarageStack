using System.Collections.Concurrent;
using System.Globalization;
using GarageStack.Core.Interfaces;
using GarageStack.Core.Models;
using Microsoft.Extensions.Localization;

namespace GarageStack.Worker.Mqtt;

/// <summary>
/// Tells the user when the gateway cannot sign in to the MG account, which stops all data until it
/// can (a changed password, or SAIC ending the session).
///
/// The gateway publishes the time of every failed sign-in on account/lastLoginError and of every
/// good one on account/lastLogin, both retained. It tries again every fifteen minutes, so the alert
/// goes out once per outage: not again while one has gone out since the last good sign-in.
/// </summary>
public sealed class AccountStatusHandler(
    ILogger logger,
    IServiceScopeFactory scopeFactory,
    IPushSender pushSender,
    IStringLocalizer<NotificationStrings> strings,
    TimeProvider timeProvider) : IMqttMessageHandler
{
    internal const string LastLoginSubtopic = "lastLogin";
    internal const string LastLoginErrorSubtopic = "lastLoginError";

    // When no good sign-in is known yet, an outage is taken to have started no earlier than this.
    internal static readonly TimeSpan UnknownLoginWindow = TimeSpan.FromDays(1);

    // Per MG account, when the gateway last signed in. Written and read by concurrent MQTT dispatches.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastLogin = new();

    public async Task<bool> TryHandleAsync(MqttMessage message, CancellationToken ct)
    {
        if (message.Account is not { } topic)
            return false;

        switch (topic.Subtopic)
        {
            case LastLoginSubtopic:
                if (TryParseTime(message.Payload, out var signedInAt))
                    _lastLogin[topic.User] = signedInAt;
                return true;

            case LastLoginErrorSubtopic:
                // A retained failure is one the broker held before this Worker subscribed: it says
                // when the gateway last failed, not that it is failing now. A live one is news, and
                // the gateway sends another every time it tries again.
                if (!message.Retain)
                    await AlertAsync(topic.User, ct);
                return true;

            default:
                return false;
        }
    }

    /// <summary>The moment the current outage can have started at the earliest: the last good sign-in.</summary>
    internal DateTime OutageStart(string saicUser) =>
        _lastLogin.TryGetValue(saicUser, out var signedInAt)
            ? signedInAt.UtcDateTime
            : (timeProvider.GetUtcNow() - UnknownLoginWindow).UtcDateTime;

    private async Task AlertAsync(string saicUser, CancellationToken ct)
    {
        // The account is an email address, so it stays out of the log.
        logger.LogWarning("The gateway could not sign in to the MG account");
        try
        {
            using var scope = scopeFactory.CreateScope();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
            if (await notifications.WasSentSinceAsync(NotificationCategories.GatewayLogin, vehicleId: null, OutageStart(saicUser), ct))
                return;

            await pushSender.SendToAllAsync(
                strings["GatewayLoginTitle"], strings["GatewayLoginBody"], ct, NotificationCategories.GatewayLogin);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send the gateway sign-in alert");
        }
    }

    // The gateway writes these as ISO 8601 in UTC.
    private static bool TryParseTime(string payload, out DateTimeOffset at) =>
        DateTimeOffset.TryParse(payload, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out at);
}
