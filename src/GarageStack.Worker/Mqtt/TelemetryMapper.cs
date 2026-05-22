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

        bool? asBool = payload switch
        {
            "true" or "True" or "1" or "on" or "open" or "unlocked" or "front" or "blowingonly" => true,
            "false" or "False" or "0" or "off" or "closed" or "locked" => false,
            _ => null
        };

        switch (subtopic)
        {
            // Fuel (saic-mqtt-gateway: drivetrain/fossilFuel/*)
            case "drivetrain/fossilFuel/percentage":
            case "drivetrain/fuelLevel":
            case "drivetrain/fuelLevelPercent":
                snapshot.FuelLevelPercent = double.IsNaN(numeric) ? null : numeric;
                break;

            case "drivetrain/fossilFuel/range":
            case "drivetrain/fuelRange":
                snapshot.FuelRangeKm = double.IsNaN(numeric) ? null : numeric;
                break;

            case "drivetrain/mileage":
            case "drivetrain/odometer":
                snapshot.OdometerKm = double.IsNaN(numeric) ? null : numeric;
                break;

            case "drivetrain/running":
                snapshot.EngineRunning = asBool;
                break;

            case "drivetrain/speed":
            case "location/speed":
                snapshot.Speed = double.IsNaN(numeric) ? null : numeric;
                break;

            case "drivetrain/auxiliaryBatteryVoltage":
            case "12v/batteryVoltage":
            case "battery/voltage":
                snapshot.BatteryVoltage = double.IsNaN(numeric) ? null : numeric;
                break;

            // EV / PHEV
            case "drivetrain/soc":
                snapshot.EvSocPercent = double.IsNaN(numeric) ? null : numeric;
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

            // Location — gateway publishes a compound JSON object, not separate topics
            case "location/position":
                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("latitude", out var lat))
                        snapshot.Latitude = lat.GetDouble();
                    if (root.TryGetProperty("longitude", out var lon))
                        snapshot.Longitude = lon.GetDouble();
                }
                catch
                {
                    return false;
                }
                break;

            case "location/latitude":
                snapshot.Latitude = double.IsNaN(numeric) ? null : numeric;
                break;
            case "location/longitude":
                snapshot.Longitude = double.IsNaN(numeric) ? null : numeric;
                break;
            case "location/heading":
                snapshot.Heading = double.IsNaN(numeric) ? null : numeric;
                break;

            // Climate
            case "climate/remoteClimateState":
            case "climate/on":
            case "climate/active":
                snapshot.ClimateOn = asBool;
                break;
            case "climate/interiorTemperature":
                snapshot.InteriorTemperature = double.IsNaN(numeric) ? null : numeric;
                break;
            case "climate/remoteTemperature":
                snapshot.RemoteTemperature = double.IsNaN(numeric) ? null : numeric;
                break;
            case "climate/exteriorTemperature":
                snapshot.ExteriorTemperature = double.IsNaN(numeric) ? null : numeric;
                break;

            // Tyres
            case "tyres/frontLeftPressure":
                snapshot.TyrePressureFrontLeft = double.IsNaN(numeric) ? null : numeric;
                break;
            case "tyres/frontRightPressure":
                snapshot.TyrePressureFrontRight = double.IsNaN(numeric) ? null : numeric;
                break;
            case "tyres/rearLeftPressure":
                snapshot.TyrePressureRearLeft = double.IsNaN(numeric) ? null : numeric;
                break;
            case "tyres/rearRightPressure":
                snapshot.TyrePressureRearRight = double.IsNaN(numeric) ? null : numeric;
                break;

            // Daily efficiency stats
            case "drivetrain/mileageOfTheDay":
                snapshot.MileageOfTheDay = double.IsNaN(numeric) ? null : numeric;
                break;
            case "drivetrain/powerUsageOfDay":
                snapshot.PowerUsageOfDay = double.IsNaN(numeric) ? null : numeric;
                break;
            case "drivetrain/mileageSinceLastCharge":
                snapshot.MileageSinceLastCharge = double.IsNaN(numeric) ? null : numeric;
                break;

            // HV drivetrain
            case "drivetrain/voltage":
                snapshot.HvVoltage = double.IsNaN(numeric) ? null : numeric;
                break;
            case "drivetrain/current":
                snapshot.HvCurrent = double.IsNaN(numeric) ? null : numeric;
                break;
            case "drivetrain/power":
                snapshot.HvPower = double.IsNaN(numeric) ? null : numeric;
                break;
            case "drivetrain/soc_kwh":
                snapshot.HvSocKwh = double.IsNaN(numeric) ? null : numeric;
                break;
            case "drivetrain/totalBatteryCapacity":
                snapshot.HvTotalCapacityKwh = double.IsNaN(numeric) ? null : numeric;
                break;
            case "drivetrain/powerUsageSinceLastCharge":
                snapshot.PowerUsageSinceLastCharge = double.IsNaN(numeric) ? null : numeric;
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
                snapshot.HeatedSeatFrontLeft = double.IsNaN(numeric) ? null : (int)numeric;
                break;
            case "climate/heatedSeatsFrontRightLevel":
                snapshot.HeatedSeatFrontRight = double.IsNaN(numeric) ? null : (int)numeric;
                break;
            case "climate/rearWindowDefrosterHeating":
                snapshot.RearWindowDefroster = asBool;
                break;

            default:
                return false;
        }

        return true;
    }
}
