using System.Text.Json;
using GarageStack.Core.Models;

namespace GarageStack.Worker.Mqtt;

public static class TelemetryMapper
{
    /// <returns>true if the subtopic was recognised and a field was set; false for metadata/unknown topics.</returns>
    public static bool ApplyMessage(TelemetrySnapshot snapshot, string subtopic, string payload)
    {
        if (!double.TryParse(payload, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var numeric))
            numeric = double.NaN;

        static double? N(double v) => double.IsFinite(v) ? v : null;

        bool? asBool = payload switch
        {
            "true" or "True" or "1" or "on" or "open" or "unlocked" or "front" or "blowingonly" or "online" => true,
            "false" or "False" or "0" or "off" or "closed" or "locked" or "offline" => false,
            _ => null
        };

        switch (subtopic)
        {
            // Fuel (saic-mqtt-gateway: drivetrain/fossilFuel/*)
            case "drivetrain/fossilFuel/percentage":
            case "drivetrain/fuelLevel":
            case "drivetrain/fuelLevelPercent":
                snapshot.FuelLevelPercent = N(numeric);
                break;

            case "drivetrain/fossilFuel/range":
            case "drivetrain/fuelRange":
                snapshot.FuelRangeKm = N(numeric);
                break;

            case "drivetrain/mileage":
            case "drivetrain/odometer":
                snapshot.OdometerKm = N(numeric);
                break;

            case "drivetrain/running":
                snapshot.EngineRunning = asBool;
                break;

            case "drivetrain/speed":
            case "location/speed":
                snapshot.Speed = N(numeric);
                break;

            case "drivetrain/auxiliaryBatteryVoltage":
            case "12v/batteryVoltage":
            case "battery/voltage":
                snapshot.BatteryVoltage = N(numeric);
                break;

            // EV / PHEV
            case "drivetrain/soc":
                snapshot.EvSocPercent = N(numeric);
                break;

            case "drivetrain/charging":
                snapshot.IsCharging = asBool;
                break;

            // Doors
            case "doors/locked":
                snapshot.IsLocked = asBool;
                break;
            case "doors/driver":
                snapshot.DriverDoorOpen = asBool;
                break;
            case "doors/passenger":
                snapshot.PassengerDoorOpen = asBool;
                break;
            case "doors/rearLeft":
                snapshot.RearLeftDoorOpen = asBool;
                break;
            case "doors/rearRight":
                snapshot.RearRightDoorOpen = asBool;
                break;
            case "doors/boot":
            case "doors/trunk":
                snapshot.TrunkOpen = asBool;
                break;
            case "doors/bonnet":
            case "doors/hood":
                snapshot.BonnetOpen = asBool;
                break;

            // Windows
            case "windows/driver":
                snapshot.DriverWindowOpen = asBool;
                break;
            case "windows/passenger":
                snapshot.PassengerWindowOpen = asBool;
                break;
            case "windows/rearLeft":
                snapshot.RearLeftWindowOpen = asBool;
                break;
            case "windows/rearRight":
                snapshot.RearRightWindowOpen = asBool;
                break;
            case "windows/sunRoof":
                snapshot.SunRoofOpen = asBool;
                break;

            // Location - gateway publishes a compound JSON object, not separate topics
            case "location/position":
                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("latitude", out var lat) && lat.TryGetDouble(out var latVal))
                        snapshot.Latitude = N(latVal);
                    if (root.TryGetProperty("longitude", out var lon) && lon.TryGetDouble(out var lonVal))
                        snapshot.Longitude = N(lonVal);
                }
                catch
                {
                    return false;
                }
                break;

            case "location/latitude":
                snapshot.Latitude = N(numeric);
                break;
            case "location/longitude":
                snapshot.Longitude = N(numeric);
                break;
            case "location/heading":
                snapshot.Heading = N(numeric);
                break;

            // Climate
            case "climate/remoteClimateState":
            case "climate/on":
            case "climate/active":
                snapshot.ClimateOn = asBool;
                break;
            case "climate/interiorTemperature":
                snapshot.InteriorTemperature = N(numeric);
                break;
            case "climate/remoteTemperature":
                snapshot.RemoteTemperature = N(numeric);
                break;
            case "climate/exteriorTemperature":
                snapshot.ExteriorTemperature = N(numeric);
                break;

            // Tyres
            case "tyres/frontLeftPressure":
                snapshot.TyrePressureFrontLeft = N(numeric);
                break;
            case "tyres/frontRightPressure":
                snapshot.TyrePressureFrontRight = N(numeric);
                break;
            case "tyres/rearLeftPressure":
                snapshot.TyrePressureRearLeft = N(numeric);
                break;
            case "tyres/rearRightPressure":
                snapshot.TyrePressureRearRight = N(numeric);
                break;

            // Daily efficiency stats
            case "drivetrain/mileageOfTheDay":
                snapshot.MileageOfTheDay = N(numeric);
                break;
            case "drivetrain/powerUsageOfDay":
                snapshot.PowerUsageOfDay = N(numeric);
                break;
            case "drivetrain/mileageSinceLastCharge":
                snapshot.MileageSinceLastCharge = N(numeric);
                break;

            // HV drivetrain
            case "drivetrain/voltage":
                snapshot.HvVoltage = N(numeric);
                break;
            case "drivetrain/current":
                snapshot.HvCurrent = N(numeric);
                break;
            case "drivetrain/power":
                snapshot.HvPower = N(numeric);
                break;
            case "drivetrain/soc_kwh":
                snapshot.HvSocKwh = N(numeric);
                break;
            case "drivetrain/totalBatteryCapacity":
                snapshot.HvTotalCapacityKwh = N(numeric);
                break;
            case "drivetrain/powerUsageSinceLastCharge":
                snapshot.PowerUsageSinceLastCharge = N(numeric);
                break;
            case "drivetrain/chargerConnected":
                snapshot.ChargerConnected = asBool;
                break;
            case "drivetrain/hvBatteryActive":
                snapshot.HvBatteryActive = asBool;
                break;

            // Lights
            case "lights/mainBeam":
                snapshot.LightsMainBeam = asBool;
                break;
            case "lights/dippedBeam":
                snapshot.LightsDippedBeam = asBool;
                break;
            case "lights/side":
                snapshot.LightsSide = asBool;
                break;

            // Climate extras
            case "climate/heatedSeatsFrontLeftLevel":
                snapshot.HeatedSeatFrontLeft = double.IsFinite(numeric) ? (int)numeric : (int?)null;
                break;
            case "climate/heatedSeatsFrontRightLevel":
                snapshot.HeatedSeatFrontRight = double.IsFinite(numeric) ? (int)numeric : (int?)null;
                break;
            case "climate/rearWindowDefrosterHeating":
                snapshot.RearWindowDefroster = asBool;
                break;

            // Online / availability
            case "available":
                snapshot.IsAvailable = asBool;
                break;

            case "refresh/lastVehicleState":
                if (DateTime.TryParse(payload, null, System.Globalization.DateTimeStyles.RoundtripKind, out var lvs))
                    snapshot.LastVehicleStateAt = lvs;
                break;

            case "refresh/lastChargeState":
                if (DateTime.TryParse(payload, null, System.Globalization.DateTimeStyles.RoundtripKind, out var lcs))
                    snapshot.LastChargeStateAt = lcs;
                break;

            // Active journey
            case "drivetrain/currentJourney/distance":
                snapshot.CurrentJourneyDistance = N(numeric);
                break;

            case "drivetrain/currentJourney":
                try
                {
                    using var cjDoc = JsonDocument.Parse(payload);
                    if (cjDoc.RootElement.TryGetProperty("distance", out var dist) && dist.TryGetDouble(out var dv))
                        snapshot.CurrentJourneyDistance = N(dv);
                }
                catch
                {
                    return false;
                }
                break;

            // Charging session
            case "drivetrain/chargingType":
                snapshot.ChargingType = payload;
                break;

            case "drivetrain/chargingCableLock":
                snapshot.ChargingCableLock = asBool;
                break;

            case "drivetrain/remainingChargingTime":
                snapshot.RemainingChargingTime = double.IsFinite(numeric) ? (int)numeric : (int?)null;
                break;

            case "drivetrain/lastChargeEndingPower":
                snapshot.LastChargeEndingPower = N(numeric);
                break;

            case "drivetrain/charging/lastEnd":
                // Unix epoch seconds
                if (double.IsFinite(numeric))
                    snapshot.ChargingLastEndAt = DateTimeOffset.FromUnixTimeSeconds((long)numeric).UtcDateTime;
                break;

            case "drivetrain/chargingSchedule":
                try
                {
                    using var csDoc = JsonDocument.Parse(payload);
                    if (csDoc.RootElement.TryGetProperty("mode", out var csm)) snapshot.ChargingScheduleMode = csm.GetString();
                    if (csDoc.RootElement.TryGetProperty("startTime", out var css)) snapshot.ChargingScheduleStartTime = css.GetString();
                    if (csDoc.RootElement.TryGetProperty("endTime", out var cse)) snapshot.ChargingScheduleEndTime = cse.GetString();
                }
                catch
                {
                    return false;
                }
                break;

            case "bms/chargeStatus":
                snapshot.BmsChargeStatus = payload;
                break;

            case "ccu/onboardChargerPlugStatus":
                snapshot.OnboardChargerPlugStatus = double.IsFinite(numeric) ? (int)numeric : (int?)null;
                break;

            case "ccu/offboardChargerPlugStatus":
                snapshot.OffboardChargerPlugStatus = double.IsFinite(numeric) ? (int)numeric : (int?)null;
                break;

            // OBC (onboard charger)
            case "obc/current":
                snapshot.ObcCurrent = N(numeric);
                break;
            case "obc/voltage":
                snapshot.ObcVoltage = N(numeric);
                break;
            case "obc/powerSinglePhase":
                snapshot.ObcPowerSinglePhase = N(numeric);
                break;
            case "obc/powerThreePhase":
                snapshot.ObcPowerThreePhase = N(numeric);
                break;

            // Battery heating
            case "drivetrain/batteryHeating":
                snapshot.BatteryHeating = asBool;
                break;

            case "drivetrain/batteryHeatingSchedule/mode":
                snapshot.BatteryHeatingScheduleMode = payload;
                break;

            case "drivetrain/batteryHeatingSchedule/startTime":
                snapshot.BatteryHeatingScheduleStartTime = payload;
                break;

            case "drivetrain/batteryHeatingSchedule":
                try
                {
                    using var bhDoc = JsonDocument.Parse(payload);
                    if (bhDoc.RootElement.TryGetProperty("mode", out var m))
                        snapshot.BatteryHeatingScheduleMode = m.GetString();
                    if (bhDoc.RootElement.TryGetProperty("startTime", out var st))
                        snapshot.BatteryHeatingScheduleStartTime = st.GetString();
                }
                catch
                {
                    return false;
                }
                break;

            // Location extras
            case "location/elevation":
                snapshot.Elevation = N(numeric);
                break;

            default:
                return false;
        }

        return true;
    }
}
