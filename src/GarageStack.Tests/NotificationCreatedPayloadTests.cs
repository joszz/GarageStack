using System.Text.Json;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

/// <summary>
/// The Worker serializes this onto the notification_created channel and the Api deserializes it
/// again before broadcasting over SignalR, so the round-trip is what matters here.
/// </summary>
public class NotificationCreatedPayloadTests
{
    private static AppNotification Notification() => new()
    {
        Id = 7,
        Title = "Engine started",
        Body = "Your car has been started.",
        CreatedAt = new DateTime(2026, 9, 16, 8, 30, 0, DateTimeKind.Utc),
        Category = NotificationCategories.EngineStart,
        VehicleId = 1,
    };

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var payload = NotificationCreatedPayload.From(Notification(), unreadCount: 3);

        var restored = NotificationCreatedPayload.FromJson(payload.ToJson());

        Assert.Equal(payload, restored);
    }

    // The panel splits notifications into an active and an archived tab. A payload without the
    // flag left a live notification matching neither, so it only appeared after a refetch.
    [Fact]
    public void Json_CarriesIsArchived_SoTheLiveNotificationLandsInTheActiveTab()
    {
        var payload = NotificationCreatedPayload.From(Notification(), unreadCount: 1);

        using var doc = JsonDocument.Parse(payload.ToJson());

        Assert.True(doc.RootElement.TryGetProperty("isArchived", out var isArchived));
        Assert.False(isArchived.GetBoolean());
    }

    [Fact]
    public void Json_UsesCamelCaseNames_MatchingTheFrontendModel()
    {
        var payload = NotificationCreatedPayload.From(Notification(), unreadCount: 2);

        using var doc = JsonDocument.Parse(payload.ToJson());
        var root = doc.RootElement;

        Assert.Equal(7, root.GetProperty("id").GetInt32());
        Assert.Equal("engine-start", root.GetProperty("category").GetString());
        Assert.Equal(2, root.GetProperty("unreadCount").GetInt32());
        Assert.Equal(1, root.GetProperty("vehicleId").GetInt32());
    }
}
