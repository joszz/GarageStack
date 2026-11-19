using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>
/// Rebuilds charge sessions from telemetry, using charging-state transitions and the gateway's
/// reported start/end times. If the charging state was missed, a complete reported interval with
/// an observed increase in state of charge or battery energy can still recover the session.
/// </summary>
public static class ChargeSessionHistory
{
    /// <summary>
    /// How far before and after a period the readings are taken, so a session that started before
    /// it or ends after it is still seen whole. A full charge from empty on a 2.3 kW socket takes
    /// about a day and a half.
    /// </summary>
    public static readonly TimeSpan SessionLookaround = TimeSpan.FromDays(2);

    /// <summary>The longest stretch the power curve is read for: a session plus its margins.</summary>
    public static readonly TimeSpan LongestPowerSpan = TimeSpan.FromDays(3);

    /// <summary>
    /// Whether a drivetrain has charge sessions. A plain hybrid never charges from a plug; a car
    /// whose type is not known yet gets them, as the browser shows the page for it too.
    /// </summary>
    public static bool Applies(string vehicleType) => vehicleType != "hev";

    /// <summary>
    /// The sessions in the readings that overlap [<paramref name="from"/>, <paramref name="to"/>),
    /// newest first.
    /// </summary>
    public static IReadOnlyList<ChargeSession> Build(
        IEnumerable<ChargeSessionReading> readings, DateTime from, DateTime to) =>
        Build(readings)
            .Where(s => s.StartedAt < to && (s.EndedAt is not { } end || end >= from))
            .ToArray();

    /// <summary>Every session in the readings, newest first.</summary>
    public static IReadOnlyList<ChargeSession> Build(IEnumerable<ChargeSessionReading> readings)
    {
        var orderedReadings = new List<ChargeSessionReading>();
        DateTime? newestChargeStateAt = null;
        foreach (var reading in readings.OrderBy(r => r.RecordedAt).ThenBy(r => r.Id))
        {
            // The gateway can replay retained charging topics after reconnecting. Ignore a poll
            // older than the newest charge-state poll, including when rebuilding reported times.
            if (reading.LastChargeStateAt is { } stamp)
            {
                if (newestChargeStateAt is { } newest && stamp < newest)
                    continue;
                newestChargeStateAt = stamp;
            }
            orderedReadings.Add(reading);
        }

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
        double? maxPowerKw = null;

        foreach (var reading in orderedReadings)
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
                sessionType ??= ChargingKind.FromGateway(chargingType);
                maxPowerKw = Max(maxPowerKw, reading.PowerKw);
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
                sessionType = ChargingKind.FromGateway(chargingType);
                maxPowerKw = reading.PowerKw;
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
                    sessionLatitude, sessionLongitude, maxPowerKw));
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
                sessionLatitude, sessionLongitude, maxPowerKw));

        AddReportedSessions(orderedReadings, sessions);
        return sessions.OrderByDescending(s => s.StartedAt).ToArray();
    }

    private static void AddReportedSessions(
        IReadOnlyList<ChargeSessionReading> readings, List<ChargeSession> sessions)
    {
        var reported = readings
            .Where(r => r.ChargingLastStartAt is not null && r.ChargingLastEndAt is not null)
            .GroupBy(r => (StartedAt: r.ChargingLastStartAt!.Value, EndedAt: r.ChargingLastEndAt!.Value))
            .Select(g => g.FirstOrDefault(r => r.RecordedAt >= g.Key.EndedAt))
            .OfType<ChargeSessionReading>();

        foreach (var report in reported)
        {
            var start = report.ChargingLastStartAt!.Value;
            var end = report.ChargingLastEndAt!.Value;
            if (end <= start ||
                end - start > SessionLookaround ||
                report.RecordedAt < end ||
                report.RecordedAt - end > SessionLookaround ||
                sessions.Any(s => s.StartedAt < end && (s.EndedAt ?? DateTime.MaxValue) > start))
                continue;

            var before = readings.LastOrDefault(r => r.RecordedAt <= start);
            var after = readings.FirstOrDefault(r => r.RecordedAt >= end);
            if (after is null || after.RecordedAt - end > SessionLookaround)
                continue;

            var startSoc = before?.EvSocPercent;
            var endSoc = after.EvSocPercent;
            var startBatteryKwh = before?.HvSocKwh;
            var endBatteryKwh = after.HvSocKwh;
            if (!(startSoc is { } startPercent && endSoc is { } endPercent && endPercent > startPercent) &&
                !(startBatteryKwh is { } startKwh && endBatteryKwh is { } endKwh && endKwh > startKwh))
                continue;

            var interval = readings.Where(r => r.RecordedAt >= start && r.RecordedAt <= end).ToArray();
            var type = interval.Select(r => ChargingKind.FromGateway(r.ChargingType)).FirstOrDefault(t => t is not null);
            var powers = interval.Select(r => r.PowerKw).Where(p => p is not null).ToArray();
            var maxPowerKw = powers.Length > 0 ? powers.Max() : null;
            sessions.Add(CreateSession(
                start, end,
                startSoc, endSoc, type,
                startBatteryKwh, endBatteryKwh, after.OdometerKm,
                after.Latitude, after.Longitude, maxPowerKw));
        }
    }

    /// <summary>The charging power over time, from the readings of one session.</summary>
    public static IReadOnlyList<ChargeSessionPowerPoint> PowerCurve(IEnumerable<ChargeSessionReading> readings) =>
        readings
            .OrderBy(r => r.RecordedAt).ThenBy(r => r.Id)
            .Select(r => r.PowerKw is { } kw ? new ChargeSessionPowerPoint(r.RecordedAt, kw) : null)
            .OfType<ChargeSessionPowerPoint>()
            .ToArray();

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
        double? maxPowerKw)
    {
        double? batteryEnergyGainedKwh = startBatteryKwh is { } start && endBatteryKwh is { } end
            ? end - start
            : null;
        return new ChargeSession(
            startedAt, endedAt, startSoc, endSoc, chargingType,
            odometerKm, latitude, longitude, startBatteryKwh, endBatteryKwh,
            batteryEnergyGainedKwh, maxPowerKw);
    }

    private static double? Max(double? current, double? candidate) =>
        candidate is { } c && (current is not { } m || c > m) ? c : current;
}
