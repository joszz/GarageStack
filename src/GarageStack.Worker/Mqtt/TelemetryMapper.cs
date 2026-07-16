using System.Globalization;
using System.Text.Json;
using GarageStack.Core.Models;

namespace GarageStack.Worker.Mqtt;

// Maps saic-mqtt-gateway MQTT subtopics onto TelemetrySnapshot fields. Several fields below
// accept more than one subtopic name (e.g. FuelLevelPercent from both "drivetrain/fossilFuel/
// percentage" and "drivetrain/fuelLevel"). Checked against the pinned gateway version's own
// topic list (src/mqtt_topics.py in SAIC-iSmart-API/saic-python-mqtt-gateway): in every such
// case, exactly one of the aliases matches a topic the current gateway actually publishes, and
// the other(s) do not appear in it at all - e.g. "doors/boot"/"doors/bonnet" are current,
// "doors/trunk"/"doors/hood" are not. These are compatibility names for an older gateway
// version or a differently-configured/forked one, not topics the current gateway emits
// alongside its canonical name. Safe to drop an alias only after confirming no supported
// gateway version still uses it.
public static class TelemetryMapper
{
    /// <returns>true if the subtopic was recognised and a field was set; false for metadata/unknown topics.</returns>
    public static bool ApplyMessage(TelemetrySnapshot snapshot, string subtopic, string payload)
    {
        if (!double.TryParse(payload, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeric))
            numeric = double.NaN;

        bool? asBool = payload switch
        {
            "true" or "True" or "1" or "on" or "open" or "unlocked" or "front" or "blowingonly" or "online" => true,
            "false" or "False" or "0" or "off" or "closed" or "locked" or "offline" => false,
            _ => null
        };

        // Each Apply* method returns null when the subtopic isn't part of its domain (try the next
        // one), or a final true/false when it is (recognised-and-mapped, or a malformed JSON payload).
        return ApplyDrivetrainBasics(snapshot, subtopic, numeric, asBool)
            ?? ApplyDoors(snapshot, subtopic, asBool)
            ?? ApplyWindows(snapshot, subtopic, asBool)
            ?? ApplyLocation(snapshot, subtopic, payload, numeric)
            ?? ApplyClimate(snapshot, subtopic, numeric, asBool)
            ?? ApplyTyres(snapshot, subtopic, numeric)
            ?? ApplyHvDrivetrainAndEfficiency(snapshot, subtopic, numeric, asBool)
            ?? ApplyLights(snapshot, subtopic, asBool)
            ?? ApplyAvailabilityAndJourney(snapshot, subtopic, payload, numeric, asBool)
            ?? ApplyChargingSession(snapshot, subtopic, payload, numeric, asBool)
            ?? ApplyObcAndBatteryHeating(snapshot, subtopic, payload, numeric, asBool)
            ?? false;
    }

    private static double? N(double v) => double.IsFinite(v) ? v : null;

    // Applies a compound-JSON payload (e.g. location/position, chargingSchedule) via the given
    // callback. Returns false, rather than throwing, when the payload isn't valid JSON so callers
    // can treat a malformed message the same way as any other recognised-but-bad subtopic.
    private static bool TryApplyJson(string payload, Action<JsonElement> apply)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            apply(doc.RootElement);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool? ApplyDrivetrainBasics(TelemetrySnapshot s, string subtopic, double numeric, bool? asBool)
    {
        switch (subtopic)
        {
            // Fuel (saic-mqtt-gateway: drivetrain/fossilFuel/*)
            case "drivetrain/fossilFuel/percentage":
            case "drivetrain/fuelLevel":
            case "drivetrain/fuelLevelPercent":
                s.FuelLevelPercent = N(numeric);
                return true;

            case "drivetrain/fossilFuel/range":
            case "drivetrain/fuelRange":
                s.FuelRangeKm = N(numeric);
                return true;

            case "drivetrain/mileage":
            case "drivetrain/odometer":
                s.OdometerKm = N(numeric);
                return true;

            case "drivetrain/running":
                s.EngineRunning = asBool;
                return true;

            case "drivetrain/speed":
            case "location/speed":
                s.Speed = N(numeric);
                return true;

            case "drivetrain/auxiliaryBatteryVoltage":
            case "12v/batteryVoltage":
            case "battery/voltage":
                s.BatteryVoltage = N(numeric);
                return true;

            // EV / PHEV
            case "drivetrain/soc":
                s.EvSocPercent = N(numeric);
                return true;

            case "drivetrain/charging":
                s.IsCharging = asBool;
                return true;

            default:
                return null;
        }
    }

    private static bool? ApplyDoors(TelemetrySnapshot s, string subtopic, bool? asBool)
    {
        switch (subtopic)
        {
            case "doors/locked":
                s.IsLocked = asBool;
                return true;
            case "doors/driver":
                s.DriverDoorOpen = asBool;
                return true;
            case "doors/passenger":
                s.PassengerDoorOpen = asBool;
                return true;
            case "doors/rearLeft":
                s.RearLeftDoorOpen = asBool;
                return true;
            case "doors/rearRight":
                s.RearRightDoorOpen = asBool;
                return true;
            case "doors/boot":
            case "doors/trunk":
                s.TrunkOpen = asBool;
                return true;
            case "doors/bonnet":
            case "doors/hood":
                s.BonnetOpen = asBool;
                return true;

            default:
                return null;
        }
    }

    private static bool? ApplyWindows(TelemetrySnapshot s, string subtopic, bool? asBool)
    {
        switch (subtopic)
        {
            case "windows/driver":
                s.DriverWindowOpen = asBool;
                return true;
            case "windows/passenger":
                s.PassengerWindowOpen = asBool;
                return true;
            case "windows/rearLeft":
                s.RearLeftWindowOpen = asBool;
                return true;
            case "windows/rearRight":
                s.RearRightWindowOpen = asBool;
                return true;
            case "windows/sunRoof":
                s.SunRoofOpen = asBool;
                return true;

            default:
                return null;
        }
    }

    private static bool? ApplyLocation(TelemetrySnapshot s, string subtopic, string payload, double numeric)
    {
        switch (subtopic)
        {
            // Gateway publishes a compound JSON object, not separate topics
            case "location/position":
                return TryApplyJson(payload, root =>
                {
                    if (root.TryGetProperty("latitude", out var lat) && lat.TryGetDouble(out var latVal))
                        s.Latitude = N(latVal);
                    if (root.TryGetProperty("longitude", out var lon) && lon.TryGetDouble(out var lonVal))
                        s.Longitude = N(lonVal);
                });

            case "location/latitude":
                s.Latitude = N(numeric);
                return true;
            case "location/longitude":
                s.Longitude = N(numeric);
                return true;
            case "location/heading":
                s.Heading = N(numeric);
                return true;

            // Location extras
            case "location/elevation":
                s.Elevation = N(numeric);
                return true;

            default:
                return null;
        }
    }

    private static bool? ApplyClimate(TelemetrySnapshot s, string subtopic, double numeric, bool? asBool)
    {
        switch (subtopic)
        {
            case "climate/remoteClimateState":
            case "climate/on":
            case "climate/active":
                s.ClimateOn = asBool;
                return true;
            case "climate/interiorTemperature":
                s.InteriorTemperature = N(numeric);
                return true;
            case "climate/remoteTemperature":
                s.RemoteTemperature = N(numeric);
                return true;
            case "climate/exteriorTemperature":
                s.ExteriorTemperature = N(numeric);
                return true;

            // Climate extras
            case "climate/heatedSeatsFrontLeftLevel":
                s.HeatedSeatFrontLeft = double.IsFinite(numeric) ? (int)numeric : (int?)null;
                return true;
            case "climate/heatedSeatsFrontRightLevel":
                s.HeatedSeatFrontRight = double.IsFinite(numeric) ? (int)numeric : (int?)null;
                return true;
            case "climate/rearWindowDefrosterHeating":
                s.RearWindowDefroster = asBool;
                return true;

            default:
                return null;
        }
    }

    private static bool? ApplyTyres(TelemetrySnapshot s, string subtopic, double numeric)
    {
        switch (subtopic)
        {
            case "tyres/frontLeftPressure":
                s.TyrePressureFrontLeft = N(numeric);
                return true;
            case "tyres/frontRightPressure":
                s.TyrePressureFrontRight = N(numeric);
                return true;
            case "tyres/rearLeftPressure":
                s.TyrePressureRearLeft = N(numeric);
                return true;
            case "tyres/rearRightPressure":
                s.TyrePressureRearRight = N(numeric);
                return true;

            default:
                return null;
        }
    }

    private static bool? ApplyHvDrivetrainAndEfficiency(TelemetrySnapshot s, string subtopic, double numeric, bool? asBool)
    {
        switch (subtopic)
        {
            // Daily efficiency stats
            case "drivetrain/mileageOfTheDay":
                s.MileageOfTheDay = N(numeric);
                return true;
            case "drivetrain/powerUsageOfDay":
                s.PowerUsageOfDay = N(numeric);
                return true;
            case "drivetrain/mileageSinceLastCharge":
                s.MileageSinceLastCharge = N(numeric);
                return true;

            // HV drivetrain
            case "drivetrain/voltage":
                s.HvVoltage = N(numeric);
                return true;
            case "drivetrain/current":
                s.HvCurrent = N(numeric);
                return true;
            case "drivetrain/power":
                s.HvPower = N(numeric);
                return true;
            case "drivetrain/soc_kwh":
                s.HvSocKwh = N(numeric);
                return true;
            case "drivetrain/totalBatteryCapacity":
                s.HvTotalCapacityKwh = N(numeric);
                return true;
            case "drivetrain/powerUsageSinceLastCharge":
                s.PowerUsageSinceLastCharge = N(numeric);
                return true;
            case "drivetrain/chargerConnected":
                s.ChargerConnected = asBool;
                return true;
            case "drivetrain/hvBatteryActive":
                s.HvBatteryActive = asBool;
                return true;

            default:
                return null;
        }
    }

    private static bool? ApplyLights(TelemetrySnapshot s, string subtopic, bool? asBool)
    {
        switch (subtopic)
        {
            case "lights/mainBeam":
                s.LightsMainBeam = asBool;
                return true;
            case "lights/dippedBeam":
                s.LightsDippedBeam = asBool;
                return true;
            case "lights/side":
                s.LightsSide = asBool;
                return true;

            default:
                return null;
        }
    }

    private static bool? ApplyAvailabilityAndJourney(TelemetrySnapshot s, string subtopic, string payload, double numeric, bool? asBool)
    {
        switch (subtopic)
        {
            // Online / availability
            case "available":
                s.IsAvailable = asBool;
                return true;

            case "refresh/lastVehicleState":
                if (DateTimeOffset.TryParse(payload, null, DateTimeStyles.RoundtripKind, out var lvs))
                    s.LastVehicleStateAt = lvs.UtcDateTime;
                return true;

            case "refresh/lastChargeState":
                if (DateTimeOffset.TryParse(payload, null, DateTimeStyles.RoundtripKind, out var lcs))
                    s.LastChargeStateAt = lcs.UtcDateTime;
                return true;

            // Active journey
            case "drivetrain/currentJourney/distance":
                s.CurrentJourneyDistance = N(numeric);
                return true;

            case "drivetrain/currentJourney":
                return TryApplyJson(payload, root =>
                {
                    if (root.TryGetProperty("distance", out var dist) && dist.TryGetDouble(out var dv))
                        s.CurrentJourneyDistance = N(dv);
                });

            default:
                return null;
        }
    }

    private static bool? ApplyChargingSession(TelemetrySnapshot s, string subtopic, string payload, double numeric, bool? asBool)
    {
        switch (subtopic)
        {
            case "drivetrain/chargingType":
                s.ChargingType = payload;
                return true;

            case "drivetrain/chargingCableLock":
                s.ChargingCableLock = asBool;
                return true;

            case "drivetrain/remainingChargingTime":
                s.RemainingChargingTime = double.IsFinite(numeric) ? (int)numeric : (int?)null;
                return true;

            case "drivetrain/lastChargeEndingPower":
                s.LastChargeEndingPower = N(numeric);
                return true;

            case "drivetrain/charging/lastEnd":
                // Unix epoch seconds
                if (double.IsFinite(numeric))
                    s.ChargingLastEndAt = DateTimeOffset.FromUnixTimeSeconds((long)numeric).UtcDateTime;
                return true;

            case "drivetrain/chargingSchedule":
                return TryApplyJson(payload, root =>
                {
                    if (root.TryGetProperty("mode", out var csm)) s.ChargingScheduleMode = csm.GetString();
                    if (root.TryGetProperty("startTime", out var css)) s.ChargingScheduleStartTime = css.GetString();
                    if (root.TryGetProperty("endTime", out var cse)) s.ChargingScheduleEndTime = cse.GetString();
                });

            case "bms/chargeStatus":
                s.BmsChargeStatus = payload;
                return true;

            case "ccu/onboardChargerPlugStatus":
                s.OnboardChargerPlugStatus = double.IsFinite(numeric) ? (int)numeric : (int?)null;
                return true;

            case "ccu/offboardChargerPlugStatus":
                s.OffboardChargerPlugStatus = double.IsFinite(numeric) ? (int)numeric : (int?)null;
                return true;

            default:
                return null;
        }
    }

    private static bool? ApplyObcAndBatteryHeating(TelemetrySnapshot s, string subtopic, string payload, double numeric, bool? asBool)
    {
        switch (subtopic)
        {
            // OBC (onboard charger)
            case "obc/current":
                s.ObcCurrent = N(numeric);
                return true;
            case "obc/voltage":
                s.ObcVoltage = N(numeric);
                return true;
            case "obc/powerSinglePhase":
                s.ObcPowerSinglePhase = N(numeric);
                return true;
            case "obc/powerThreePhase":
                s.ObcPowerThreePhase = N(numeric);
                return true;

            // Battery heating
            case "drivetrain/batteryHeating":
                s.BatteryHeating = asBool;
                return true;

            case "drivetrain/batteryHeatingSchedule/mode":
                s.BatteryHeatingScheduleMode = payload;
                return true;

            case "drivetrain/batteryHeatingSchedule/startTime":
                s.BatteryHeatingScheduleStartTime = payload;
                return true;

            case "drivetrain/batteryHeatingSchedule":
                return TryApplyJson(payload, root =>
                {
                    if (root.TryGetProperty("mode", out var m))
                        s.BatteryHeatingScheduleMode = m.GetString();
                    if (root.TryGetProperty("startTime", out var st))
                        s.BatteryHeatingScheduleStartTime = st.GetString();
                });

            default:
                return null;
        }
    }
}
