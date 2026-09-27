# Homepage dashboard widget

GarageStack exposes a dedicated read-only endpoint for the [gethomepage.dev](https://gethomepage.dev) [Custom API widget](https://gethomepage.dev/widgets/services/customapi/). No fork or custom widget code is required.

## 1. Generate an API key

```bash
openssl rand -base64 32
```

Set `WIDGET_API_KEY` to the generated value in your `.env` file (Docker Compose) or as a container environment variable (all-in-one / Unraid). Leave it empty to keep the endpoint disabled.

## 2. Find your VIN

Log in to GarageStack, open the browser developer tools (Network tab) and reload: the VIN is the `vin` field in the `/api/vehicles` response. It is also the 17-character segment in the MQTT topics the gateway logs, `saic/<account>/vehicles/<VIN>/...`.

## 3. Configure Homepage

Add the following block to your Homepage `services.yaml`, replacing `YOUR_GARAGESTACK_URL`, `YOUR_VIN`, and `YOUR_WIDGET_API_KEY`:

```yaml
- GarageStack:
    href: https://YOUR_GARAGESTACK_URL
    description: MG Vehicle Status
    widget:
      type: customapi
      url: https://YOUR_GARAGESTACK_URL/api/widget/YOUR_VIN/status
      headers:
        X-Widget-Key: "YOUR_WIDGET_API_KEY"
      mappings:
        - field: evSocPercent
          label: Battery
          format: percent
        - field: isCharging
          label: Charging
          format: text
        - field: exteriorTemperature
          label: Ext. Temp
          format: float
          suffix: "°C"
        - field: isLocked
          label: Locked
          format: text
```

## Available fields

The endpoint returns a flat JSON object. Numeric fields are `null` when the vehicle has not reported that value yet. String state fields are also `null` when unreported, except `anyDoorOpen` and `anyWindowOpen` which are always present. String values are localized: the language is resolved from the request in this order: query string, cookie, `Accept-Language` header, falling back to `en`. Supported languages are `en` and `nl`. To pin a language regardless of the Homepage container's locale, append `?culture=nl&ui-culture=nl` (or `en`) to the widget URL.

| Field | Type | Description |
| ------- | ------ | ----------- |
| `recordedAt` | string (ISO 8601) | Timestamp of the most recent telemetry |
| `fuelLevelPercent` | number | Fuel tank level (%) |
| `fuelRangeKm` | number | Estimated fuel range (km) |
| `evSocPercent` | number | EV / HV battery state of charge (%) |
| `electricRangeKm` | number | Distance the car estimates it can drive on the battery alone (km), on a BEV or PHEV. Never reported as 0, so an empty battery keeps its last value |
| `isCharging` | string | Charging state: `"Charging"` or `"Not charging"` |
| `chargerConnected` | string | Charger connection state: `"Plugged in"` or `"Unplugged"` |
| `mileageSinceLastCharge` | number | Distance driven since last full charge (km) |
| `hvSocKwh` | number | HV battery energy (kWh), as the gateway reports it |
| `hvTotalCapacityKwh` | number | HV battery total capacity (kWh), as the gateway reports it |
| `hvVoltage` | number | HV system voltage (V) |
| `hvCurrent` | number | HV system current (A) |
| `hvPower` | number | HV system power (W) |
| `odometerKm` | number | Total odometer reading (km) |
| `mileageOfTheDayKm` | number | Distance driven today (km) |
| `powerUsageOfDayKwh` | number | Energy used today (kWh). On a plain hybrid this counter holds the trip computer's fuel total in hundredths of a litre instead, so divide by 100 for litres |
| `electricSharePercent` | number | % of today's distance driven on electric power (PHEV) |
| `isLocked` | string | Lock state: `"Locked"` or `"Unlocked"` |
| `engineRunning` | string | Engine state: `"Engine on"` or `"Engine off"` |
| `climateOn` | string | Remote climate state: `"On"` or `"Off"` |
| `driverDoorOpen` | string | Driver door state: `"Open"` or `"Closed"` |
| `passengerDoorOpen` | string | Passenger door state: `"Open"` or `"Closed"` |
| `rearLeftDoorOpen` | string | Rear left door state: `"Open"` or `"Closed"` |
| `rearRightDoorOpen` | string | Rear right door state: `"Open"` or `"Closed"` |
| `trunkOpen` | string | Boot / trunk state: `"Open"` or `"Closed"` |
| `bonnetOpen` | string | Bonnet / hood state: `"Open"` or `"Closed"` |
| `anyDoorOpen` | string | `"Open"` if any door, boot, or bonnet is open, otherwise `"Closed"` (never null) |
| `driverWindowOpen` | string | Driver window state: `"Open"` or `"Closed"` |
| `passengerWindowOpen` | string | Passenger window state: `"Open"` or `"Closed"` |
| `rearLeftWindowOpen` | string | Rear left window state: `"Open"` or `"Closed"` |
| `rearRightWindowOpen` | string | Rear right window state: `"Open"` or `"Closed"` |
| `sunRoofOpen` | string | Sunroof state: `"Open"` or `"Closed"` |
| `anyWindowOpen` | string | `"Open"` if any window or sunroof is open, otherwise `"Closed"` (never null) |
| `batteryVoltage` | number | 12V auxiliary battery voltage (V) |
| `interiorTemperature` | number | Interior temperature (°C) |
| `exteriorTemperature` | number | Exterior temperature (°C) |
| `tyrePressureFrontLeft` | number | Front-left tyre pressure (bar) |
| `tyrePressureFrontRight` | number | Front-right tyre pressure (bar) |
| `tyrePressureRearLeft` | number | Rear-left tyre pressure (bar) |
| `tyrePressureRearRight` | number | Rear-right tyre pressure (bar) |
| `lightsMainBeam` | string | Main beam headlights state: `"On"` or `"Off"` |
| `lightsDippedBeam` | string | Dipped beam headlights state: `"On"` or `"Off"` |
| `lightsSide` | string | Side / parking lights state: `"On"` or `"Off"` |
| `speedKmh` | number | Current vehicle speed (km/h) |
| `currentJourneyDistanceKm` | number | Distance driven in the current trip (km) |
| `isAvailable` | string | Cloud reachability: `"Online"` or `"Offline"` |
| `lastVehicleStateAt` | string (ISO 8601) | Timestamp the car last pushed state to SAIC cloud |
| `lastChargeStateAt` | string (ISO 8601) | Timestamp the car last pushed charge state to SAIC cloud |
| `remainingChargingTime` | number | Estimated minutes remaining to reach charge limit |
| `chargingType` | string | Charging type as reported by the gateway (e.g. `"AC"`, `"DC"`) |
| `chargingCableLock` | string | Cable lock state: `"Locked"` or `"Unlocked"` |
| `obcPowerSinglePhase` | number | Onboard charger single-phase AC power (kW) |
| `obcPowerThreePhase` | number | Onboard charger three-phase AC power (kW) |
| `batteryHeating` | string | Battery pre-heating state: `"On"` or `"Off"` |
| `batteryHeatingScheduleMode` | string | Battery heating schedule mode (e.g. `"off"`) |
| `batteryHeatingScheduleStartTime` | string | Battery heating schedule start time (HH:MM) |
| `elevation` | number | Vehicle elevation above sea level (m) |
| `bmsChargeStatus` | string | BMS charge status string (e.g. `"UNPLUGGED"`, `"CHARGING"`) |
| `lastChargeEndingPower` | number | State of charge (%) when the last charge session ended |
| `chargingLastEndAt` | string (ISO 8601) | Timestamp the last charge session ended |
| `chargingScheduleMode` | string | Scheduled charging mode (e.g. `"DISABLED"`, `"UNTIL_CONFIGURED_TIME"`) |
| `chargingScheduleStartTime` | string | Scheduled charge start time (HH:MM) |
| `chargingScheduleEndTime` | string | Scheduled charge end time (HH:MM) |
| `onboardChargerPlugStatus` | number | Onboard charger plug presence status (raw integer) |
| `offboardChargerPlugStatus` | number | Offboard (DC) charger plug presence status (raw integer) |
