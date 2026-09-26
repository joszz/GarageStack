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
}

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
  chargingLastEndAt: string | null
  chargingScheduleMode: string | null
  chargingScheduleStartTime: string | null
  chargingScheduleEndTime: string | null
  onboardChargerPlugStatus: number | null
  offboardChargerPlugStatus: number | null
}

/**
 * The chart-relevant slice of a snapshot returned by the history endpoint. Mirrors
 * TelemetryHistoryPoint on the API; the statistics page reads nothing else per point.
 */
export interface TelemetryHistoryPoint {
  recordedAt: string
  fuelLevelPercent: number | null
  evSocPercent: number | null
  powerUsageOfDay: number | null
  batteryVoltage: number | null
  climateOn: boolean | null
  isCharging: boolean | null
  tyrePressureFrontLeft: number | null
  tyrePressureFrontRight: number | null
  tyrePressureRearLeft: number | null
  tyrePressureRearRight: number | null
  mileageOfTheDay: number | null
  mileageSinceLastCharge: number | null
  hvSocKwh: number | null
  hvTotalCapacityKwh: number | null
  powerUsageSinceLastCharge: number | null
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
}

export const vehicleApi = {
  list: () => request<Vehicle[]>('/api/vehicles'),
  status: (vin: string) => request<TelemetrySnapshot>(`/api/vehicles/${vin}/status`),
  tyrePressureThresholds: () =>
    request<TyrePressureThresholds>('/api/vehicles/tyre-pressure-thresholds'),
  config: (vin: string) => request<Record<string, string>>(`/api/vehicles/${vin}/config`),
  history: (vin: string, from?: string, to?: string) => {
    const query = buildQuery({ from, to })
    return request<TelemetryHistoryPoint[]>(`/api/vehicles/${vin}/history${query}`)
  },
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
