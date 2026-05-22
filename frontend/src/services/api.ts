import { useAuthStore } from '@/stores/auth'
import router from '@/router'

const BASE_URL = import.meta.env.VITE_API_URL ?? ''

function authHeaders(): Record<string, string> {
  const auth = useAuthStore()
  return auth.accessToken ? { Authorization: `Bearer ${auth.accessToken}` } : {}
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    ...options,
    headers: { ...authHeaders(), ...(options?.headers ?? {}) },
    credentials: 'include',
  })

  if (res.status === 401) {
    const auth = useAuthStore()
    const refreshed = await auth.refresh()
    if (!refreshed) {
      await auth.logout()
      router.replace({ name: 'login' })
      throw new Error('Session expired')
    }
    const retry = await fetch(`${BASE_URL}${path}`, {
      ...options,
      headers: { ...authHeaders(), ...(options?.headers ?? {}) },
      credentials: 'include',
    })
    if (!retry.ok) throw new Error(`API error ${retry.status}: ${path}`)
    if (retry.status === 204) return undefined as T
    return retry.json() as Promise<T>
  }

  if (!res.ok) throw new Error(`API error ${res.status}: ${path}`)
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

async function send(path: string, method: string, body?: unknown): Promise<void> {
  const res = await fetch(`${BASE_URL}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    credentials: 'include',
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  if (res.status === 401) {
    const auth = useAuthStore()
    const refreshed = await auth.refresh()
    if (!refreshed) {
      await auth.logout()
      router.replace({ name: 'login' })
      throw new Error('Session expired')
    }
    const retry = await fetch(`${BASE_URL}${path}`, {
      method,
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      credentials: 'include',
      body: body !== undefined ? JSON.stringify(body) : undefined,
    })
    if (!retry.ok) throw new Error(`API error ${retry.status}: ${path}`)
    return
  }

  if (!res.ok) throw new Error(`API error ${res.status}: ${path}`)
}

export interface Vehicle {
  id: number
  vin: string
  model: string | null
  series: string | null
  saicUser: string | null
  configJson: string | null
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

export const vehicleApi = {
  list: () => request<Vehicle[]>('/api/vehicles'),
  status: (vin: string) => request<TelemetrySnapshot>(`/api/vehicles/${vin}/status`),
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
