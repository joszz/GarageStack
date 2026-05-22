using GarageStack.Core.Models;
using GarageStack.Worker.Mqtt;

namespace GarageStack.Tests;

public class TelemetryMapperTests
{
    [Fact]
    public void ApplyMessage_FuelLevel_SetsPercent()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "drivetrain/fuelLevel", "72.5");
        Assert.Equal(72.5, snapshot.FuelLevelPercent);
    }

    [Fact]
    public void ApplyMessage_Locked_SetsIsLocked()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "doors/locked", "true");
        Assert.True(snapshot.IsLocked);
    }

    [Fact]
    public void ApplyMessage_Unlocked_SetsIsLockedFalse()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "doors/locked", "false");
        Assert.False(snapshot.IsLocked);
    }

    [Fact]
    public void ApplyMessage_Latitude_SetsCoordinate()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "location/latitude", "52.3676");
        Assert.Equal(52.3676, snapshot.Latitude);
    }

    [Fact]
    public void ApplyMessage_UnknownSubtopic_LeavesSnapshotUnchanged()
    {
        var snapshot = new TelemetrySnapshot();
        TelemetryMapper.ApplyMessage(snapshot, "unknown/topic", "somevalue");
        Assert.Null(snapshot.FuelLevelPercent);
        Assert.Null(snapshot.IsLocked);
    }
}
