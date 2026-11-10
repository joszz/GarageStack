import { request, requestJson, send } from './apiCore'

export type DawarichState = 'ok' | 'badKey' | 'unreachable' | 'disabled'
export interface DawarichStatus {
  configured: boolean
  status: DawarichState
}
export interface LinkedPhoneTracker {
  trackerId: string
  createdAt: string
}
export interface PhoneTracker {
  trackerId: string
  lastSeenAt: string
}
export interface PhoneTrackers {
  linked: LinkedPhoneTracker[]
  available: PhoneTracker[]
  error: 'badKey' | 'unreachable' | null
}

const path = (vin: string) => `/api/vehicles/${encodeURIComponent(vin)}/phone-trackers`
export const dawarichApi = {
  status: () => request<DawarichStatus>('/api/dawarich/status'),
  trackers: (vin: string) => request<PhoneTrackers>(path(vin)),
  save: (vin: string, trackerIds: string[]) =>
    requestJson<LinkedPhoneTracker[]>(path(vin), 'PUT', { trackerIds }),
  backfill: (vin: string, from: string) => send(`${path(vin)}/backfill`, 'POST', { from }),
}
