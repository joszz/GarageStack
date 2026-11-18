using System.Globalization;
using System.Text.Json;
using GarageStack.Core.Models;
using GarageStack.Worker.Services;

namespace GarageStack.Tests;

// What a push message tells the service worker: the text, and the buttons it can put on the
// notification with what it needs to carry each one out.
public class PushPayloadTests
{
    private const string Vin = "FAKEVN00000000001";

    private static JsonElement Serialized(PushPayload.Message message) =>
        JsonDocument.Parse(PushPayload.Serialize(message)).RootElement;

    [Fact]
    public void UnlockedParked_OffersLockForTheCar()
    {
        var message = PushPayload.For("Car left unlocked", "Parked and unlocked",
            NotificationCategories.UnlockedParked, 2, Vin, WorkerLocalizer.Notifications());

        var json = Serialized(message);
        Assert.Equal(Vin, json.GetProperty("vin").GetString());
        var action = Assert.Single(json.GetProperty("actions").EnumerateArray());
        Assert.Equal("lock", action.GetProperty("name").GetString());
        Assert.Equal("Lock", action.GetProperty("title").GetString());
        Assert.Equal("Lock sent to the car.", action.GetProperty("sent").GetString());
        Assert.False(json.TryGetProperty("maintenanceItemId", out _));
    }

    [Theory]
    [InlineData("maintenance-overdue-12")]
    [InlineData("maintenance-due-soon-12")]
    public void MaintenanceReminder_OffersMarkAsDoneForItsItem(string category)
    {
        var json = Serialized(PushPayload.For("Maintenance overdue", "Oil is overdue",
            category, 1, Vin, WorkerLocalizer.Notifications()));

        Assert.Equal(12, json.GetProperty("maintenanceItemId").GetInt32());
        var action = Assert.Single(json.GetProperty("actions").EnumerateArray());
        Assert.Equal("maintenance-done", action.GetProperty("name").GetString());
        Assert.Equal("Mark as done", action.GetProperty("title").GetString());
    }

    [Fact]
    public void CategoryWithoutActions_CarriesNoCarAndNoButtons()
    {
        var json = Serialized(PushPayload.For("Charging complete", "Done", NotificationCategories.ChargingComplete,
            3, Vin, WorkerLocalizer.Notifications()));

        Assert.Equal("Charging complete", json.GetProperty("title").GetString());
        Assert.Equal("Done", json.GetProperty("body").GetString());
        Assert.Equal("charging-complete", json.GetProperty("category").GetString());
        Assert.Equal(3, json.GetProperty("unreadCount").GetInt32());
        Assert.False(json.TryGetProperty("vin", out _));
        Assert.False(json.TryGetProperty("actions", out _));
    }

    [Fact]
    public void UnknownCar_OffersNoButtons()
    {
        var json = Serialized(PushPayload.For("Car left unlocked", "Parked and unlocked",
            NotificationCategories.UnlockedParked, null, null, WorkerLocalizer.Notifications()));

        Assert.False(json.TryGetProperty("actions", out _));
        Assert.False(json.TryGetProperty("unreadCount", out _));
    }

    [Fact]
    public void Labels_FollowTheNotificationLanguage()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("nl");
        try
        {
            var message = PushPayload.For("t", "b", NotificationCategories.UnlockedParked, null, Vin, WorkerLocalizer.Notifications());

            Assert.Equal("Vergrendelen", Assert.Single(message.Actions!).Title);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
