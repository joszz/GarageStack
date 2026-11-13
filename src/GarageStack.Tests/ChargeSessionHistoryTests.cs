using GarageStack.Core.Helpers;
using GarageStack.Core.Models;

namespace GarageStack.Tests;

public class ChargeSessionHistoryTests
{
    private static readonly DateTime T0 = new(2026, 3, 1, 18, 0, 0, DateTimeKind.Utc);

    private static ChargeSessionReading Reading(
        int minute,
        bool? charging = null,
        double? soc = null,
        DateTime? chargeStateAt = null,
        double? single = null,
        double? three = null,
        double? hv = null) =>
        new(minute, T0.AddMinutes(minute), chargeStateAt, charging, soc, null, null, null,
            null, null, null, null, hv, single, three);

    // ── ChargerPower: the gateway's figures are kW, and only one phase figure is right ──

    [Theory]
    // Issue #570: a single-phase charger, the battery taking 4.0 kW.
    [InlineData(4.3427, 2.5071, -4.0, 4.3427)]
    // A three-phase charger, where the single-phase figure reads 1.7 times the truth.
    [InlineData(19.05, 11.0, -10.4, 11.0)]
    // Without the battery's power there is nothing to choose by: the three-phase figure, as before.
    [InlineData(4.3, 2.5, null, 2.5)]
    [InlineData(4.3, 2.5, 0.0, 2.5)]
    [InlineData(3.68, null, -3.4, 3.68)]
    [InlineData(null, 11.0, -10.4, 11.0)]
    // DC bypasses the onboard charger, which reads zero: the battery's power is the charge.
    [InlineData(0.0, 0.0, -74.5, 74.5)]
    [InlineData(null, null, -50.0, 50.0)]
    public void ChargerPower_PicksTheFigureClosestToTheBattery(double? single, double? three, double? hv, double expected) =>
        Assert.Equal(expected, ChargerPower.Kw(single, three, hv));

    [Fact]
    public void ChargerPower_IsNullWithoutAnyFigure()
    {
        Assert.Null(ChargerPower.Kw(null, null, null));
        Assert.Null(ChargerPower.Kw(0, 0, 0));
        Assert.Null(ChargerPower.Kw(double.NaN, null, null));
    }

    [Theory]
    [InlineData("bev", true)]
    [InlineData("phev", true)]
    [InlineData("unknown", true)]
    [InlineData("hev", false)]
    public void Applies_ToEveryCarButAPlainHybrid(string vehicleType, bool expected) =>
        Assert.Equal(expected, ChargeSessionHistory.Applies(vehicleType));

    // ── Build ──

    [Fact]
    public void Build_TakesPowerInKilowatts()
    {
        var sessions = ChargeSessionHistory.Build(
        [
            Reading(0, charging: true, soc: 40, single: 7.1),
            Reading(10, single: 7.4),
            Reading(20, charging: false, soc: 50),
        ]);

        Assert.Equal(7.4, Assert.Single(sessions).MaxPowerKw);
    }

    [Fact]
    public void Build_IgnoresAReplayOfAnOlderPoll()
    {
        // The gateway restarts after a charge ended and publishes its retained topics again,
        // charging=true among them, stamped with the poll they came from.
        var sessions = ChargeSessionHistory.Build(
        [
            Reading(0, charging: true, soc: 40, chargeStateAt: T0),
            Reading(60, charging: false, soc: 80, chargeStateAt: T0.AddMinutes(60)),
            Reading(120, charging: true, soc: 40, chargeStateAt: T0),
            Reading(125, charging: false, soc: 80, chargeStateAt: T0.AddMinutes(125)),
        ]);

        var session = Assert.Single(sessions);
        Assert.Equal(T0, session.StartedAt);
        Assert.Equal(T0.AddMinutes(60), session.EndedAt);
    }

    [Fact]
    public void Build_KeepsARowWithoutAStamp()
    {
        var sessions = ChargeSessionHistory.Build(
        [
            Reading(0, charging: true, soc: 40, chargeStateAt: T0),
            Reading(30, charging: false, soc: 60),
        ]);

        Assert.Equal(T0.AddMinutes(30), Assert.Single(sessions).EndedAt);
    }

    [Fact]
    public void Build_KeepsOnlySessionsThatOverlapThePeriod()
    {
        List<ChargeSessionReading> readings =
        [
            Reading(0, charging: true, soc: 40),
            Reading(60, charging: false, soc: 60),
            Reading(24 * 60, charging: true, soc: 30),
            Reading(25 * 60, charging: false, soc: 70),
        ];

        // A period that starts while the first session runs keeps it.
        Assert.Equal(2, ChargeSessionHistory.Build(readings, T0.AddMinutes(30), T0.AddDays(2)).Count);
        Assert.Equal(T0.AddDays(1), Assert.Single(ChargeSessionHistory.Build(readings, T0.AddHours(2), T0.AddDays(2))).StartedAt);
        Assert.Empty(ChargeSessionHistory.Build(readings, T0.AddHours(2), T0.AddHours(3)));
    }

    [Fact]
    public void PowerCurve_LeavesOutReadingsWithoutPower()
    {
        var curve = ChargeSessionHistory.PowerCurve(
        [
            Reading(10, single: 7.0, hv: -6.3),
            Reading(0, charging: true, single: 3.0),
            Reading(5, soc: 41),
        ]);

        Assert.Equal([T0, T0.AddMinutes(10)], curve.Select(point => point.RecordedAt));
        Assert.Equal([3.0, 7.0], curve.Select(point => point.PowerKw));
    }
}
