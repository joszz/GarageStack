import { request, send, buildQuery } from '@/services/apiCore'

/** Drivetrains the API can report, detected from the vehicle's hardware version. */
export type VehicleType = 'hev' | 'phev' | 'bev' | 'unknown'

export interface Vehicle {
  id: number
  vin: string
  model: string | null
  series: string | null
  createdAt: string
  /** Drivetrain the API detected from the vehicle's hardware version. */
  vehicleType: VehicleType
  /**
   * The traction battery's real usable capacity when the deployment configures one, overriding
   * the EV-sized figure the gateway assumes. Null leaves that figure in place for a plug-in car
   * and hides kWh entirely for a hybrid, where it is known to be wrong.
   */
  hvBatteryCapacityKwh: number | null
  /** Whether the car takes a charge target, by the gateway's own test; it drops one on any other. */
  supportsTargetSoc: boolean
}

/**
 * The remote climate state as the gateway reports it: off, on (normal climate), front (front
 * window defrost) or blowingonly (fan only).
 */
export type ClimateMode = 'on' | 'off' | 'front' | 'blowingonly'

export interface TelemetrySnapshot {
  id: number
  vehicleId: number
  recordedAt: string
  fuelLevelPercent: number | null
  fuelRangeKm: number | null
  odometerKm: number | null
  isLocked: boolean | null
  engineRunning: boolean | null
  climateOn: boolean | null
  /** Only the gateway's remoteClimateState names the mode, so it can be null while climateOn is set. */
  climateMode: ClimateMode | null
  driverDoorOpen: boolean | null
  passengerDoorOpen: boolean | null
  rearLeftDoorOpen: boolean | null
  rearRightDoorOpen: boolean | null
  trunkOpen: boolean | null
  bonnetOpen: boolean | null
  driverWindowOpen: boolean | null
  passengerWindowOpen: boolean | null
  rearLeftWindowOpen: boolean | null
  rearRightWindowOpen: boolean | null
  latitude: number | null
  longitude: number | null
  speed: number | null
  heading: number | null
  batteryVoltage: number | null
  interiorTemperature: number | null
  exteriorTemperature: number | null
  evSocPercent: number | null
  electricRangeKm: number | null
  isCharging: boolean | null
  sunRoofOpen: boolean | null
  tyrePressureFrontLeft: number | null
  tyrePressureFrontRight: number | null
  tyrePressureRearLeft: number | null
  tyrePressureRearRight: number | null
  mileageOfTheDay: number | null
  powerUsageOfDay: number | null
  mileageSinceLastCharge: number | null
  hvVoltage: number | null
  hvCurrent: number | null
  hvPower: number | null
  hvSocKwh: number | null
  hvTotalCapacityKwh: number | null
  powerUsageSinceLastCharge: number | null
  chargerConnected: boolean | null
  hvBatteryActive: boolean | null
  lightsMainBeam: boolean | null
  lightsDippedBeam: boolean | null
  lightsSide: boolean | null
  remoteTemperature: number | null
  heatedSeatFrontLeft: number | null
  heatedSeatFrontRight: number | null
  rearWindowDefroster: boolean | null
  isAvailable: boolean | null
  lastVehicleStateAt: string | null
  lastChargeStateAt: string | null
  currentJourneyDistance: number | null
  chargingType: string | null
  chargingCableLock: boolean | null
  remainingChargingTime: number | null
  obcCurrent: number | null
  obcVoltage: number | null
  obcPowerSinglePhase: number | null
  obcPowerThreePhase: number | null
  batteryHeating: boolean | null
  batteryHeatingScheduleMode: string | null
  batteryHeatingScheduleStartTime: string | null
  elevation: number | null
  bmsChargeStatus: string | null
  lastChargeEndingPower: number | null
  chargingLastStartAt: string | null
  chargingLastEndAt: string | null
  // The newest reason on record, which can belong to a charge that ended long ago; the Worker
  // judges a stop by its own poll, so nothing here should present it as current.
  chargingStopReason: string | null
  socTargetPercent: number | null
  chargeCurrentLimit: string | null
  chargingScheduleMode: string | null
  chargingScheduleStartTime: string | null
  chargingScheduleEndTime: string | null
  onboardChargerPlugStatus: number | null
  offboardChargerPlugStatus: number | null
}

/**
 * One local day for the statistics page, as the API works it out from every reading of that day
 * (DailyStat). A value is null when the car reported none of it that day. `counterTotal` is in the
 * counter's own unit: kWh on a plug-in car, hundredths of a litre on a plain hybrid (utils/energy).
 */
export interface DailyStat {
  /** The local day, "YYYY-MM-DD". */
  date: string
  evSocPercent: number | null
  fuelLevelPercent: number | null
  tyrePressureFrontLeft: number | null
  tyrePressureFrontRight: number | null
  tyrePressureRearLeft: number | null
  tyrePressureRearRight: number | null
  batteryVoltage: number | null
  counterTotal: number | null
}

export interface VehicleAggregateStats {
  climateUsagePct: number | null
  climateOnSnapshots: number
  totalClimateSnapshots: number
}

export interface TripPoint {
  recordedAt: string
  latitude: number
  longitude: number
  speed: number | null
}

/** A trip without its fixes, with the figures the API reads off them. */
export interface TripSummary {
  index: number
  /** The saved trip's id, or null for one the Worker has not saved yet, such as the one being driven. */
  id: number | null
  startedAt: string
  endedAt: string
  distanceKm: number
  pointCount: number
  /** Where the trip ended. */
  endLatitude: number | null
  endLongitude: number | null
  /** The highest speed any fix reported, null when none carried a speed. */
  maxSpeedKmh: number | null
  /** The mean of the speeds reported while moving, null when none was. */
  avgMovingSpeedKmh: number | null
  /** How many fixes that average is taken over, to weigh it against other trips'. */
  movingSpeedSamples: number
}

export interface Trip extends TripSummary {
  points: TripPoint[]
}

/**
 * The gateway's answer to a command, pushed over SignalR once the car carried it out or refused.
 * `command` is the name it was sent under (e.g. "lock"); `detail` is the gateway's reason for a
 * refusal, in its own words (English), or null.
 */
export interface CommandResult {
  command: string
  success: boolean
  detail: string | null
}

export interface TyrePressureThresholds {
  lowBar: number
  goodBar: number
  highBar: number
  /** How far one tyre may fall behind the other three over the trend's days: a slow puncture. */
  dropBar: number
}

/** Each tyre's change against the other three, in bar: negative when it lost air the others kept. */
export interface TyreChanges {
  frontLeft: number
  frontRight: number
  rearLeft: number
  rearRight: number
  /** How many days with all four readings the changes rest on. */
  days: number
}

export interface TyreHistory {
  /** A month of days, the same rows as dailyStats. */
  days: DailyStat[]
  /** Null until there are a week's readings of all four tyres. */
  trend: TyreChanges | null
  /** The days the trend is measured over. */
  trendDays: number
}

export const vehicleApi = {
  list: () => request<Vehicle[]>('/api/vehicles'),
  status: (vin: string) => request<TelemetrySnapshot>(`/api/vehicles/${vin}/status`),
  tyrePressureThresholds: () =>
    request<TyrePressureThresholds>('/api/vehicles/tyre-pressure-thresholds'),
  config: (vin: string) => request<Record<string, string>>(`/api/vehicles/${vin}/config`),
  /** One row per local day from `from` to `to` ("YYYY-MM-DD"), in the browser's time zone. */
  dailyStats: (vin: string, from: string, to: string, timeZone: string) =>
    request<DailyStat[]>(`/api/vehicles/${vin}/stats/daily${buildQuery({ from, to, timeZone })}`),
  /** A month of daily tyre pressures, in the browser's days, and each tyre's trend against the others. */
  tyreHistory: (vin: string, timeZone: string) =>
    request<TyreHistory>(`/api/vehicles/${vin}/tyres${buildQuery({ timeZone })}`),
  trips: (vin: string, from?: string, to?: string) => {
    const query = buildQuery({ from, to })
    return request<Trip[]>(`/api/vehicles/${vin}/trips${query}`)
  },
  /** The same period of trips without their fixes, for pages that only need the figures. */
  tripSummaries: (vin: string, from?: string, to?: string) => {
    const query = buildQuery({ from, to, points: false })
    return request<TripSummary[]>(`/api/vehicles/${vin}/trips${query}`)
  },
  /** The newest trip, or undefined when the vehicle has none (the API answers 204). */
  latestTrip: (vin: string) => request<Trip | undefined>(`/api/vehicles/${vin}/trips/latest`),
  sendCommand: (vin: string, command: string, value: string) =>
    send(`/api/vehicles/${vin}/commands/${command}`, 'POST', { value }),
  stats: (vin: string, from?: string, to?: string) => {
    const query = buildQuery({ from, to })
    return request<VehicleAggregateStats>(`/api/vehicles/${vin}/stats${query}`)
  },
}
