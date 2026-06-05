const BASE_URL = import.meta.env.VITE_API_URL ?? ''

let unauthorizedHandler: (() => void) | null = null
let handlingUnauthorized = false

export function setUnauthorizedHandler(handler: (() => void) | null) {
  unauthorizedHandler = handler
}

export function clearUnauthorizedState() {
  handlingUnauthorized = false
}

export class ApiError extends Error {
  status: number
  path: string

  constructor(status: number, path: string) {
    super(`API error ${status}: ${path}`)
    this.status = status
    this.path = path
  }
}

function handleResponse(res: Response, path: string) {
  if (res.status === 401 && !handlingUnauthorized) {
    handlingUnauthorized = true
    unauthorizedHandler?.()
  }
  if (!res.ok) throw new ApiError(res.status, path)
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    ...options,
    credentials: 'include',
  })

  handleResponse(res, path)
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

async function send(path: string, method: string, body?: unknown): Promise<void> {
  const res = await fetch(`${BASE_URL}${path}`, {
    method,
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  handleResponse(res, path)
}

export interface LoginResponse {
  username: string
  expiresAtUtc: string
}

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
    const params = new URLSearchParams()
    if (from) params.set('from', from)
    if (to) params.set('to', to)
    return request<TelemetrySnapshot[]>(`/api/vehicles/${vin}/history?${params}`)
  },
  trips: (vin: string, from?: string, to?: string) => {
    const params = new URLSearchParams()
    if (from) params.set('from', from)
    if (to) params.set('to', to)
    return request<Trip[]>(`/api/vehicles/${vin}/trips?${params}`)
  },
  sendCommand: (vin: string, command: string, value: string) =>
    send(`/api/vehicles/${vin}/commands/${command}`, 'POST', { value }),
}

export const pushApi = {
  getVapidPublicKey: () => request<{ publicKey: string }>('/api/push/vapid-public-key'),
  subscribe: (endpoint: string, p256DhKey: string, authKey: string) =>
    send('/api/push/subscribe', 'POST', { endpoint, p256DhKey, authKey }),
  unsubscribe: (endpoint: string) =>
    send(`/api/push/unsubscribe?endpoint=${encodeURIComponent(endpoint)}`, 'DELETE'),
}

export interface AppNotification {
  id: number
  title: string
  body: string
  createdAt: string
  isArchived: boolean
  category: string | null
}

export const notificationsApi = {
  list: () => request<AppNotification[]>('/api/notifications'),
  archive: (id: number) => send(`/api/notifications/${id}/archive`, 'PATCH'),
  archiveAll: () => send('/api/notifications/archive-all', 'PATCH'),
  delete: (id: number) => send(`/api/notifications/${id}`, 'DELETE'),
  deleteAll: () => send('/api/notifications', 'DELETE'),
}

export interface MeResponse {
  username: string
  expiresAtUtc: string | null
}

export const authApi = {
  login: (username: string, password: string, rememberMe = false) =>
    request<LoginResponse>('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password, rememberMe }),
    }),
  logout: () => send('/api/auth/logout', 'POST'),
  me: () => request<MeResponse>('/api/auth/me'),
}
