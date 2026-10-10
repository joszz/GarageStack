using GarageStack.Api.Endpoints;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class ClimateScheduleValidationTests
{
    private static ClimateScheduleRequest Valid() => new(
        Name: "Work", Enabled: true, StartTime: "07:30", Days: [1, 2, 3, 4, 5], TimeZoneId: "Europe/Amsterdam",
        Mode: ClimateScheduleMode.On, TemperatureC: 21, RearDefroster: false, SeatLeftLevel: 2, SeatRightLevel: 0,
        OnlyBelowC: 5, OnlyAboveC: 25);

    private static string? CodeFor(ClimateScheduleRequest request) =>
        ClimateScheduleEndpoints.Validate(request).Error?.Code;

    [Fact]
    public void AValidRequest_IsReadIntoItsTimeDaysAndZone()
    {
        var (valid, error) = ClimateScheduleEndpoints.Validate(Valid());

        Assert.Null(error);
        Assert.Equal(new TimeOnly(7, 30), valid!.StartTime);
        Assert.Equal(ClimateScheduleDays.Monday | ClimateScheduleDays.Tuesday | ClimateScheduleDays.Wednesday
            | ClimateScheduleDays.Thursday | ClimateScheduleDays.Friday, valid.Days);
        Assert.Equal("Europe/Amsterdam", valid.Zone.Id);
    }

    [Fact]
    public void NoDays_IsAOneOffSchedule()
    {
        Assert.Null(CodeFor(Valid() with { Days = [] }));
        Assert.Null(CodeFor(Valid() with { Days = null }));
    }

    [Fact]
    public void NoCondition_IsValid()
    {
        Assert.Null(CodeFor(Valid() with { OnlyBelowC = null, OnlyAboveC = null }));
    }

    [Fact]
    public void ANameTooLong_IsRefused()
    {
        Assert.Equal("climateSchedule.nameTooLong", CodeFor(Valid() with { Name = new string('a', 51) }));
    }

    [Theory]
    [InlineData("7:30")]
    [InlineData("07:30:00")]
    [InlineData("24:00")]
    [InlineData("")]
    public void AStartTimeNotAsHoursAndMinutes_IsRefused(string startTime)
    {
        Assert.Equal("climateSchedule.startTimeInvalid", CodeFor(Valid() with { StartTime = startTime }));
    }

    [Fact]
    public void DaysOutsideTheWeek_AreRefused()
    {
        Assert.Equal("climateSchedule.daysInvalid", CodeFor(Valid() with { Days = [0, 1] }));
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("")]
    public void AnUnknownTimeZone_IsRefused(string zone)
    {
        Assert.Equal("climateSchedule.timeZoneUnknown", CodeFor(Valid() with { TimeZoneId = zone }));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(29)]
    public void ATemperatureTheCarDoesNotTake_IsRefused(int temperatureC)
    {
        Assert.Equal("climateSchedule.temperatureOutOfRange", CodeFor(Valid() with { TemperatureC = temperatureC }));
    }

    [Fact]
    public void ASeatLevelAboveHigh_IsRefused()
    {
        Assert.Equal("climateSchedule.seatLevelOutOfRange", CodeFor(Valid() with { SeatRightLevel = 4 }));
    }

    [Fact]
    public void AnOutsideTemperatureOutOfRange_IsRefused()
    {
        Assert.Equal("climateSchedule.thresholdOutOfRange", CodeFor(Valid() with { OnlyBelowC = -60 }));
    }

    [Theory]
    [InlineData(20.0, 10.0)]
    [InlineData(10.0, 10.0)]
    public void AConditionThatNeverSkips_IsRefused(double onlyBelowC, double onlyAboveC)
    {
        Assert.Equal("climateSchedule.conditionAlwaysTrue",
            CodeFor(Valid() with { OnlyBelowC = onlyBelowC, OnlyAboveC = onlyAboveC }));
    }

    [Fact]
    public void ToDto_SendsTimesAsUtcAndTheStartAsHoursAndMinutes()
    {
        var schedule = new ClimateSchedule
        {
            Id = 3,
            StartTime = new TimeOnly(6, 5),
            Days = ClimateScheduleDays.Saturday,
            // As the in-memory demo database hands it back: no kind.
            NextRunUtc = new DateTime(2026, 10, 10, 4, 5, 0, DateTimeKind.Unspecified),
        };

        var dto = ClimateScheduleEndpoints.ToDto(schedule);

        Assert.Equal("06:05", dto.StartTime);
        Assert.Equal([6], dto.Days);
        Assert.Equal(DateTimeKind.Utc, dto.NextRunUtc!.Value.Kind);
    }
}
