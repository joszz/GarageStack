using System.Text.Json;
using GarageStack.Core.Helpers;
using GarageStack.Core.Models;
using Door = GarageStack.Core.Helpers.VehicleEquipment.Door;

namespace GarageStack.Tests;

// Codes as saic-python-mqtt-gateway 0.14.0-rc5 reads them (src/vehicle_info.py), with the values
// real cars send in its tests/fixtures/vehicle_model_configurations.json.
public class VehicleEquipmentTests
{
    private static Vehicle Car(params (string Key, string Value)[] config) => new()
    {
        Vin = "FAKEVN00000000001",
        ConfigJson = config.Length == 0 ? null : JsonSerializer.Serialize(config.ToDictionary(c => c.Key, c => c.Value)),
    };

    // The Cyberster has two doors; a missing or malformed mask keeps every door.
    [Theory]
    [InlineData("1100", Door.Driver, true)]
    [InlineData("1100", Door.Passenger, true)]
    [InlineData("1100", Door.RearLeft, false)]
    [InlineData("1100", Door.RearRight, false)]
    [InlineData("1111", Door.RearRight, true)]
    [InlineData("110", Door.RearLeft, true)]
    [InlineData("11x0", Door.RearRight, true)]
    public void HasDoor_FollowsTheMask(string mask, Door door, bool expected)
    {
        Assert.Equal(expected, VehicleEquipment.HasDoor(Car(("DOOR", mask)), door));
    }

    [Fact]
    public void HasDoor_EveryDoor_WithoutCodes()
    {
        Assert.All(Enum.GetValues<Door>(), door => Assert.True(VehicleEquipment.HasDoor(Car(), door)));
    }

    // 0 on most cars, 3 on the ZS EV and MG S9 that have one; 2 alone is not a sunroof.
    [Theory]
    [InlineData("0", false)]
    [InlineData("2", false)]
    [InlineData("3", true)]
    [InlineData("1", true)]
    [InlineData("x", true)]
    public void HasSunroof_FollowsS35(string value, bool expected)
    {
        Assert.Equal(expected, VehicleEquipment.HasSunroof(Car(("S35", value))));
    }

    [Fact]
    public void HasSunroof_WithoutTheCode()
    {
        Assert.True(VehicleEquipment.HasSunroof(Car()));
    }

    // Only the code counts, not an item name that matches it apart from case.
    [Fact]
    public void HasSunroof_IgnoresAnItemNameInAnotherCase()
    {
        Assert.True(VehicleEquipment.HasSunroof(Car(("s35", "0"))));
    }

    // The gateway's TestPowerTailgate cases.
    [Theory]
    [InlineData("MIS3E S", null, true)] // MG S6 EV
    [InlineData("EH32 S", null, false)]
    [InlineData("MIS3E L", null, false)] // its long variant
    [InlineData("EH32 S", "0", false)]
    [InlineData("MIS3E S", "0", false)]
    [InlineData("EH32 S", "2", true)]
    [InlineData("MIS3E L", "2", true)]
    [InlineData("EH32 S", "1", false)]
    [InlineData("MIS3E S", "1", true)]
    public void ReportsPowerTailgate_LikeTheApp(string series, string? ctrlBoot, bool expected)
    {
        var car = ctrlBoot is null
            ? Car(("hw_version", series))
            : Car(("hw_version", series), ("CTRLBOOT", ctrlBoot));

        Assert.Equal(expected, VehicleEquipment.ReportsPowerTailgate(car));
    }

    // The user's say wins either way; without it the codes decide.
    [Theory]
    [InlineData(null, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void HasPowerTailgate_UserFirstThenCodes(bool? setting, bool expected)
    {
        var car = Car(("hw_version", "MIS3E S"));
        car.HasPowerTailgate = setting;

        Assert.Equal(expected, VehicleEquipment.HasPowerTailgate(car));
    }

    [Fact]
    public void HasPowerTailgate_OffWithoutCodesOrSetting()
    {
        Assert.False(VehicleEquipment.HasPowerTailgate(Car()));
    }

    [Fact]
    public void WithoutMissingEquipment_ClearsTheRearDoorsAndSunroofOfATwoDoorCar()
    {
        var car = Car(("DOOR", "1100"), ("S35", "0"));
        var snapshot = AllOpen();

        var shown = VehicleEquipment.WithoutMissingEquipment(snapshot, car);

        Assert.True(shown.DriverDoorOpen);
        Assert.True(shown.PassengerDoorOpen);
        Assert.True(shown.DriverWindowOpen);
        Assert.True(shown.PassengerWindowOpen);
        Assert.True(shown.TrunkOpen);
        Assert.Null(shown.RearLeftDoorOpen);
        Assert.Null(shown.RearRightDoorOpen);
        Assert.Null(shown.RearLeftWindowOpen);
        Assert.Null(shown.RearRightWindowOpen);
        Assert.Null(shown.SunRoofOpen);
    }

    // The snapshot may be the repository's cached one, so it is never changed.
    [Fact]
    public void WithoutMissingEquipment_LeavesTheOriginalAlone()
    {
        var snapshot = AllOpen();

        var shown = VehicleEquipment.WithoutMissingEquipment(snapshot, Car(("DOOR", "1100"), ("S35", "0")));

        Assert.NotSame(snapshot, shown);
        Assert.True(snapshot.RearLeftDoorOpen);
        Assert.True(snapshot.SunRoofOpen);
    }

    [Fact]
    public void WithoutMissingEquipment_SameSnapshotWhenTheCarHasEverything()
    {
        var snapshot = AllOpen();

        Assert.Same(snapshot, VehicleEquipment.WithoutMissingEquipment(snapshot, Car(("DOOR", "1111"), ("S35", "3"))));
        Assert.Same(snapshot, VehicleEquipment.WithoutMissingEquipment(snapshot, Car()));
    }

    private static TelemetrySnapshot AllOpen() => new()
    {
        DriverDoorOpen = true,
        PassengerDoorOpen = true,
        RearLeftDoorOpen = true,
        RearRightDoorOpen = true,
        DriverWindowOpen = true,
        PassengerWindowOpen = true,
        RearLeftWindowOpen = true,
        RearRightWindowOpen = true,
        TrunkOpen = true,
        SunRoofOpen = true,
    };
}
