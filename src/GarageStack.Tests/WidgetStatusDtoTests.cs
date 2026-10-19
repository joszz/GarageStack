using GarageStack.Api;
using GarageStack.Api.Endpoints;
using GarageStack.Api.Services;
using GarageStack.Core.Models;
using Microsoft.Extensions.Localization;

namespace GarageStack.Tests;

public class WidgetStatusDtoTests
{
    private static readonly VehicleFacts NoFacts = new(null, null, null, null, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void FromSnapshot_PowerUsageOfDay_PassesKwhThrough()
    {
        // The counter goes out as the gateway publishes it (kWh on a plug-in car, hundredths of a
        // litre on a plain hybrid); dividing by 1000 turned 16.34 kWh into 0.02 (issue #284)
        var snapshot = new TelemetrySnapshot { PowerUsageOfDay = 16.337 };

        var dto = WidgetStatusDto.FromSnapshot(snapshot, NoFacts, new KeyEchoLocalizer());

        Assert.Equal(16.34, dto.PowerUsageOfDayKwh);
    }

    [Fact]
    public void FromSnapshot_NoPowerUsageOfDay_ReturnsNull()
    {
        var dto = WidgetStatusDto.FromSnapshot(new TelemetrySnapshot(), NoFacts, new KeyEchoLocalizer());

        Assert.Null(dto.PowerUsageOfDayKwh);
    }

    [Fact]
    public void FromSnapshot_CarriesTheApkTheMaintenanceAndTheLastTrip()
    {
        var facts = new VehicleFacts(
            new DateOnly(2027, 4, 26), 189, 1, true,
            "Oil change", "dueSoon", new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), 13, 850,
            new DateTime(2026, 10, 19, 7, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 19, 7, 40, 0, DateTimeKind.Utc), 42.5, false);

        var dto = WidgetStatusDto.FromSnapshot(new TelemetrySnapshot(), facts, new KeyEchoLocalizer());

        Assert.Equal((new DateOnly(2027, 4, 26), 189, 1, "Insured"), (dto.ApkExpiresOn, dto.ApkDaysLeft, dto.OpenRecalls, dto.WamInsured));
        Assert.Equal(("Oil change", "MaintenanceDueSoon", 13, 850.0),
            (dto.NextMaintenance, dto.NextMaintenanceStatus, dto.NextMaintenanceDaysLeft, dto.NextMaintenanceKmLeft));
        Assert.Equal(42.5, dto.LastTripDistanceKm);
    }

    [Fact]
    public void FromSnapshot_WithoutThoseFacts_LeavesTheirFieldsEmpty()
    {
        var dto = WidgetStatusDto.FromSnapshot(new TelemetrySnapshot(), NoFacts, new KeyEchoLocalizer());

        Assert.Null(dto.ApkDaysLeft);
        Assert.Null(dto.WamInsured);
        Assert.Null(dto.NextMaintenanceStatus);
        Assert.Null(dto.LastTripEndedAt);
    }

    private sealed class KeyEchoLocalizer : IStringLocalizer<WidgetStrings>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
