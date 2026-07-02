import { request, send, buildQuery } from '@/services/apiCore'

export interface Vehicle {
  id: number
  vin: string
  model: string | null
  series: string | null
  createdAt: string
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
  steeringWheelHeating: boolean | null
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

export interface Trip {
  index: number
  startedAt: string
  endedAt: string
  distanceKm: number
  pointCount: number
  points: TripPoint[]
}

export interface LastTripSummary {
  distanceKm: number
  recordedAt: string
}

export const vehicleApi = {
  list: () => request<Vehicle[]>('/api/vehicles'),
  status: (vin: string) => request<TelemetrySnapshot>(`/api/vehicles/${vin}/status`),
  lastTrip: (vin: string) =>
    request<LastTripSummary | undefined>(`/api/vehicles/${vin}/trips/last`),
  config: (vin: string) => request<Record<string, string>>(`/api/vehicles/${vin}/config`),
  history: (vin: string, from?: string, to?: string) => {
    const query = buildQuery({ from, to })
    return request<TelemetrySnapshot[]>(`/api/vehicles/${vin}/history${query}`)
  },
  trips: (vin: string, from?: string, to?: string) => {
    const query = buildQuery({ from, to })
    return request<Trip[]>(`/api/vehicles/${vin}/trips${query}`)
  },
  sendCommand: (vin: string, command: string, value: string) =>
    send(`/api/vehicles/${vin}/commands/${command}`, 'POST', { value }),
  stats: (vin: string, from?: string, to?: string) => {
    const query = buildQuery({ from, to })
    return request<VehicleAggregateStats>(`/api/vehicles/${vin}/stats${query}`)
  },
}
