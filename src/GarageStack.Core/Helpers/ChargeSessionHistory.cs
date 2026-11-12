using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

public static class ChargeSessionHistory
{
    public static IReadOnlyList<ChargeSession> Build(IEnumerable<ChargeSessionReading> readings)
    {
        var sessions = new List<ChargeSession>();
        bool? charging = null;
        double? soc = null;
        double? batteryKwh = null;
        double? odometerKm = null;
        double? latitude = null;
        double? longitude = null;
        string? chargingType = null;
        DateTime? lastReportedStartAt = null;
        DateTime? lastReportedEndAt = null;
        DateTime? lastTransitionAt = null;
        DateTime? startedAt = null;
        double? startSoc = null;
        double? endSoc = null;
        double? startBatteryKwh = null;
        double? endBatteryKwh = null;
        double? sessionOdometerKm = null;
        double? sessionLatitude = null;
        double? sessionLongitude = null;
        string? sessionType = null;
        List<ChargeSessionPowerPoint> powerPoints = [];

        foreach (var reading in readings.OrderBy(r => r.RecordedAt).ThenBy(r => r.Id))
        {
            soc = reading.EvSocPercent ?? soc;
            batteryKwh = reading.HvSocKwh ?? batteryKwh;
            odometerKm = reading.OdometerKm ?? odometerKm;
            latitude = reading.Latitude ?? latitude;
            longitude = reading.Longitude ?? longitude;
            chargingType = reading.ChargingType ?? chargingType;
            lastReportedStartAt = reading.ChargingLastStartAt ?? lastReportedStartAt;
            lastReportedEndAt = reading.ChargingLastEndAt ?? lastReportedEndAt;

            if (charging == true && startedAt is not null)
            {
                endSoc = soc;
                endBatteryKwh = batteryKwh;
                sessionOdometerKm = odometerKm;
                if (sessionLatitude is null || sessionLongitude is null)
                {
                    sessionLatitude = latitude;
                    sessionLongitude = longitude;
                }
                sessionType ??= chargingType;
                AddPowerPoint(powerPoints, reading);
            }

            if (reading.IsCharging is not { } isCharging)
                continue;

            if (isCharging && charging != true)
            {
                var reportedStart = lastReportedStartAt;
                var previousTransition = lastTransitionAt;
                startedAt = reportedStart is { } start &&
                    start <= reading.RecordedAt &&
                    (previousTransition is null || start >= previousTransition.Value)
                    ? start
                    : reading.RecordedAt;
                startSoc = soc;
                endSoc = soc;
                startBatteryKwh = batteryKwh;
                endBatteryKwh = batteryKwh;
                sessionOdometerKm = odometerKm;
                sessionLatitude = latitude;
                sessionLongitude = longitude;
                sessionType = chargingType;
                powerPoints = [];
                AddPowerPoint(powerPoints, reading);
            }
            else if (!isCharging && charging == true && startedAt is { } sessionStart)
            {
                var reportedEnd = lastReportedEndAt;
                var endedAt = reportedEnd is { } end &&
                    end >= sessionStart &&
                    end <= reading.RecordedAt
                    ? end
                    : reading.RecordedAt;

                endSoc = soc;
                endBatteryKwh = batteryKwh;
                sessionOdometerKm = odometerKm;
                sessions.Add(CreateSession(
                    sessionStart, endedAt, startSoc, endSoc, sessionType,
                    startBatteryKwh, endBatteryKwh, sessionOdometerKm,
                    sessionLatitude, sessionLongitude, powerPoints));
                startedAt = null;
            }
            if (charging != isCharging)
                lastTransitionAt = reading.RecordedAt;
            charging = isCharging;
        }

        if (startedAt is { } ongoingStart)
            sessions.Add(CreateSession(
                ongoingStart, null, startSoc, endSoc, sessionType,
                startBatteryKwh, endBatteryKwh, sessionOdometerKm,
                sessionLatitude, sessionLongitude, powerPoints));

        return sessions.OrderByDescending(s => s.StartedAt).ToArray();
    }

    private static ChargeSession CreateSession(
        DateTime startedAt,
        DateTime? endedAt,
        double? startSoc,
        double? endSoc,
        string? chargingType,
        double? startBatteryKwh,
        double? endBatteryKwh,
        double? odometerKm,
        double? latitude,
        double? longitude,
        IReadOnlyList<ChargeSessionPowerPoint> powerPoints)
    {
        var maxPowerKw = powerPoints.Count > 0 ? powerPoints.Max(point => point.PowerKw) : (double?)null;
        double? batteryEnergyGainedKwh = startBatteryKwh is { } start && endBatteryKwh is { } end
            ? end - start
            : null;
        return new ChargeSession(
            startedAt, endedAt, startSoc, endSoc, chargingType,
            odometerKm, latitude, longitude, startBatteryKwh, endBatteryKwh,
            batteryEnergyGainedKwh, maxPowerKw, powerPoints.ToArray());
    }

    private static void AddPowerPoint(List<ChargeSessionPowerPoint> powerPoints, ChargeSessionReading reading)
    {
        var rawPower = reading.ObcPowerThreePhase is > 0
            ? reading.ObcPowerThreePhase
            : reading.ObcPowerSinglePhase is > 0
                ? reading.ObcPowerSinglePhase
                : reading.HvPower is { } hvPower
                    ? Math.Abs(hvPower)
                    : reading.ObcPowerThreePhase ?? reading.ObcPowerSinglePhase;
        if (rawPower is { } power && double.IsFinite(power))
            powerPoints.Add(new ChargeSessionPowerPoint(reading.RecordedAt, Math.Abs(power) / 1000));
    }
}
