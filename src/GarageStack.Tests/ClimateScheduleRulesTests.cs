using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class ClimateScheduleConditionsTests
{
    private static ClimateSchedule Schedule(double? onlyBelowC = null, double? onlyAboveC = null) =>
        new() { OnlyBelowC = onlyBelowC, OnlyAboveC = onlyAboveC };

    [Fact]
    public void ACarBeingDriven_IsSkipped()
    {
        Assert.Equal(ClimateScheduleOutcome.SkippedDriving,
            ClimateScheduleConditions.SkipReason(Schedule(), engineRunning: true, exteriorC: 2));
    }

    [Fact]
    public void NoCondition_RunsWhateverTheWeather()
    {
        Assert.Null(ClimateScheduleConditions.SkipReason(Schedule(), engineRunning: false, exteriorC: 15));
    }

    [Theory]
    [InlineData(2.0, null)]
    [InlineData(30.0, null)]
    [InlineData(4.9, ClimateScheduleOutcome.SkippedTemperature)]
    public void ColdOrHot_RunsOnlyOutsideTheMildRange(double exteriorC, ClimateScheduleOutcome? expected)
    {
        // Below 3 °C or above 25 °C.
        Assert.Equal(expected,
            ClimateScheduleConditions.SkipReason(Schedule(onlyBelowC: 3, onlyAboveC: 25), engineRunning: false, exteriorC));
    }

    [Fact]
    public void OnlyAColdThreshold_SkipsAWarmDay()
    {
        Assert.Equal(ClimateScheduleOutcome.SkippedTemperature,
            ClimateScheduleConditions.SkipReason(Schedule(onlyBelowC: 5), engineRunning: null, exteriorC: 28));
    }

    [Fact]
    public void AnUnknownOutsideTemperature_Runs()
    {
        Assert.Null(ClimateScheduleConditions.SkipReason(Schedule(onlyBelowC: 5), engineRunning: null, exteriorC: null));
    }
}

public class ClimateSchedulePlanTests
{
    [Fact]
    public void NormalClimate_SetsTheTemperatureFirst_ThenTheExtrasAskedFor()
    {
        var schedule = new ClimateSchedule
        {
            Mode = ClimateScheduleMode.On,
            TemperatureC = 22,
            RearDefroster = true,
            SeatLeftLevel = 3,
            SeatRightLevel = 1,
        };

        Assert.Equal(
            [
                new ScheduledCommand("climate-temperature", "22"),
                new ScheduledCommand("climate", "on"),
                new ScheduledCommand("rear-defroster", "on"),
                new ScheduledCommand("seat-left", "3"),
                new ScheduledCommand("seat-right", "1"),
            ],
            ClimateSchedulePlan.For(schedule));
    }

    [Theory]
    [InlineData(ClimateScheduleMode.FanOnly, "blowingonly")]
    [InlineData(ClimateScheduleMode.FrontDefrost, "front")]
    public void OtherModes_SendNoTemperature_AndLeaveOutExtrasNotAskedFor(ClimateScheduleMode mode, string gatewayValue)
    {
        var schedule = new ClimateSchedule { Mode = mode, TemperatureC = 22 };

        Assert.Equal([new ScheduledCommand("climate", gatewayValue)], ClimateSchedulePlan.For(schedule));
    }

    [Fact]
    public void EveryPlannedCommand_IsOneTheGatewayTakes()
    {
        var schedule = new ClimateSchedule { Mode = ClimateScheduleMode.On, RearDefroster = true, SeatLeftLevel = 2, SeatRightLevel = 2 };

        Assert.All(ClimateSchedulePlan.For(schedule), step => Assert.Null(VehicleCommands.Validate(step.Command, step.Value)));
    }
}

public class ClimateScheduleDaysTests
{
    [Fact]
    public void IsoDays_RoundTrip()
    {
        var days = ClimateScheduleDaysExtensions.FromIsoDays([1, 5, 7]);

        Assert.Equal(ClimateScheduleDays.Monday | ClimateScheduleDays.Friday | ClimateScheduleDays.Sunday, days);
        Assert.Equal([1, 5, 7], days!.Value.ToIsoDays());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void IsoDays_OutsideTheWeek_AreRefused(int iso)
    {
        Assert.Null(ClimateScheduleDaysExtensions.FromIsoDays([1, iso]));
    }

    [Fact]
    public void IsoDays_Repeated_AreRefused()
    {
        Assert.Null(ClimateScheduleDaysExtensions.FromIsoDays([2, 2]));
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, ClimateScheduleDays.Monday)]
    [InlineData(DayOfWeek.Saturday, ClimateScheduleDays.Saturday)]
    [InlineData(DayOfWeek.Sunday, ClimateScheduleDays.Sunday)]
    public void Flag_MatchesTheWeekday(DayOfWeek day, ClimateScheduleDays flag)
    {
        Assert.Equal(flag, day.Flag());
    }
}
