using System.Text.Json;
using GarageStack.Api.Services;

namespace GarageStack.Tests;

public class HomeAssistantDiscoveryTests
{
    private const string Vin = "FAKEVN00000000001";

    private static readonly VehicleFacts Nothing = new(null, null, null, null, null, null, null, null, null, null, null, null, null);

    private static readonly VehicleFacts Everything = new(
        new DateOnly(2026, 11, 18), 30, 1, true,
        "Oil change", "dueSoon", new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), 13, 850,
        new DateTime(2026, 10, 19, 7, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 19, 7, 40, 0, DateTimeKind.Utc), 42.5, false);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void ACarWithNothingToTell_RemovesEverySensor()
    {
        var configs = HomeAssistantDiscovery.Configs(Vin, Nothing);

        Assert.NotEmpty(configs);
        Assert.All(configs, c => Assert.Equal(string.Empty, c.Payload));
    }

    [Fact]
    public void OnlyTheSensorsWithSomethingToShow_AreAnnounced()
    {
        var rdwOnly = Nothing with { ApkExpiresOn = new DateOnly(2026, 11, 18), ApkDaysLeft = 30, OpenRecalls = 0 };

        var announced = HomeAssistantDiscovery.Configs(Vin, rdwOnly)
            .Where(c => c.Payload.Length > 0)
            .Select(c => Parse(c.Payload).GetProperty("unique_id").GetString());

        Assert.Equal(
            [$"garagestack_{Vin}_apk_expires_on", $"garagestack_{Vin}_apk_days_left", $"garagestack_{Vin}_open_recalls", $"garagestack_{Vin}_wam_insured"],
            announced);
    }

    [Fact]
    public void ASensor_JoinsTheCarsDevice_AndReadsItsValueFromTheStateTopic()
    {
        var (topic, payload) = HomeAssistantDiscovery.Configs(Vin, Everything).Single(c => c.Topic.EndsWith("/apk_days_left/config", StringComparison.Ordinal));
        var config = Parse(payload);

        Assert.Equal($"homeassistant/sensor/garagestack_{Vin}/apk_days_left/config", topic);
        Assert.Equal(Vin, config.GetProperty("device").GetProperty("identifiers")[0].GetString());
        Assert.Equal($"garagestack/{Vin}/state", config.GetProperty("state_topic").GetString());
        Assert.Equal("{{ value_json.apk_days_left }}", config.GetProperty("value_template").GetString());
        Assert.Equal("d", config.GetProperty("unit_of_measurement").GetString());
    }

    [Fact]
    public void TheInsurance_IsABinarySensor()
    {
        var (topic, payload) = HomeAssistantDiscovery.Configs(Vin, Everything).Single(c => c.Topic.Contains("/wam_insured/", StringComparison.Ordinal));

        Assert.StartsWith("homeassistant/binary_sensor/", topic);
        Assert.Equal("{{ 'ON' if value_json.wam_insured else 'OFF' }}", Parse(payload).GetProperty("value_template").GetString());
    }

    [Fact]
    public void TheState_CarriesEveryValue_InTheFormsHomeAssistantReads()
    {
        var state = Parse(HomeAssistantDiscovery.State(Everything));

        Assert.Equal("2026-11-18", state.GetProperty("apk_expires_on").GetString());
        Assert.Equal(30, state.GetProperty("apk_days_left").GetInt32());
        Assert.True(state.GetProperty("wam_insured").GetBoolean());
        Assert.Equal("due_soon", state.GetProperty("next_maintenance_status").GetString());
        Assert.Equal("2026-11-01", state.GetProperty("next_maintenance_due").GetString());
        Assert.Equal("2026-10-19T07:40:00.0000000Z", state.GetProperty("last_trip_ended").GetString());
        Assert.Equal(42.5, state.GetProperty("last_trip_distance").GetDouble());
    }

    [Fact]
    public void AValueNotKnown_IsNull_SoHomeAssistantShowsItAsUnknown()
    {
        var state = Parse(HomeAssistantDiscovery.State(Nothing));

        Assert.Equal(JsonValueKind.Null, state.GetProperty("apk_days_left").ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("last_trip_ended").ValueKind);
    }
}
