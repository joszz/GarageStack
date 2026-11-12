using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

public static class ChargeSessionHistory
{
    public static IReadOnlyList<ChargeSession> Build(IEnumerable<ChargeSessionReading> readings)
    {
        var sessions = new List<ChargeSession>();
        bool? charging = null;
        double? soc = null;
        string? chargingType = null;
        DateTime? lastReportedStartAt = null;
        DateTime? lastReportedEndAt = null;
        DateTime? lastTransitionAt = null;
        DateTime? startedAt = null;
        double? startSoc = null;
        double? endSoc = null;
        string? sessionType = null;

        foreach (var reading in readings.OrderBy(r => r.RecordedAt).ThenBy(r => r.Id))
        {
            soc = reading.EvSocPercent ?? soc;
            chargingType = reading.ChargingType ?? chargingType;
            lastReportedStartAt = reading.ChargingLastStartAt ?? lastReportedStartAt;
            lastReportedEndAt = reading.ChargingLastEndAt ?? lastReportedEndAt;

            if (charging == true && startedAt is not null)
            {
                endSoc = soc;
                sessionType ??= chargingType;
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
                sessionType = chargingType;
            }
            else if (!isCharging && charging == true && startedAt is { } sessionStart)
            {
                var reportedEnd = lastReportedEndAt;
                var endedAt = reportedEnd is { } end &&
                    end >= sessionStart &&
                    end <= reading.RecordedAt
                    ? end
                    : reading.RecordedAt;

                sessions.Add(new ChargeSession(sessionStart, endedAt, startSoc, soc, sessionType));
                startedAt = null;
            }
            charging = isCharging;
            lastTransitionAt = reading.RecordedAt;
        }

        if (startedAt is { } ongoingStart)
            sessions.Add(new ChargeSession(ongoingStart, null, startSoc, endSoc, sessionType));

        return sessions.OrderByDescending(s => s.StartedAt).ToArray();
    }
}
