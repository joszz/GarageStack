import type { TelemetrySnapshot, Trip, Vehicle } from '@/services/vehicleApi'

const STORAGE_KEY = 'garagestack-last-known'
// Raised when the shape below changes, so a copy an older build saved is dropped, not misread.
const VERSION = 1

/**
 * The car as this browser last loaded it: what the dashboard shows when the server cannot be
 * reached, in an underground car park or with no signal at all. Only the newest copy is kept.
 */
export interface LastKnownState {
  /** When this browser last saved any part of it (ISO 8601). */
  savedAt: string
  vehicles: Vehicle[]
  status: TelemetrySnapshot | null
  config: Record<string, string>
  /** The newest trip without its fixes: the dashboard's cards read only its figures. */
  latestTrip: Trip | null
}

interface StoredState extends LastKnownState {
  version: number
}

function isStoredState(value: unknown): value is StoredState {
  if (typeof value !== 'object' || value === null) return false
  const state = value as Partial<StoredState>
  return (
    state.version === VERSION &&
    typeof state.savedAt === 'string' &&
    Array.isArray(state.vehicles) &&
    typeof state.config === 'object' &&
    state.config !== null &&
    (state.status === null || typeof state.status === 'object') &&
    (state.latestTrip === null || typeof state.latestTrip === 'object')
  )
}

/** The saved state, or null when there is none or it cannot be read. */
export function readLastKnownState(): LastKnownState | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw === null) return null
    const parsed: unknown = JSON.parse(raw)
    if (!isStoredState(parsed)) return null
    const { version: _, ...state } = parsed
    return state
  } catch {
    return null
  }
}

/**
 * Saves the state, stamped with `now`. A trip is kept without its fixes: a long one runs to
 * hundreds of kilobytes, and nothing that reads this copy draws a route. A browser that refuses
 * to store it (private mode, a full quota) simply has nothing to show offline.
 */
export function saveLastKnownState(
  state: Omit<LastKnownState, 'savedAt'>,
  now: Date = new Date(),
): void {
  const stored: StoredState = {
    version: VERSION,
    savedAt: now.toISOString(),
    vehicles: state.vehicles,
    status: state.status,
    config: state.config,
    latestTrip: state.latestTrip ? { ...state.latestTrip, points: [] } : null,
  }
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(stored))
  } catch {
    // Nothing to do: the live page does not depend on this copy.
  }
}

/** Forgotten on sign-out, with the rest of the session: the next account must not see this car. */
export function forgetLastKnownState(): void {
  try {
    localStorage.removeItem(STORAGE_KEY)
  } catch {
    // A browser that cannot remove it could not have stored it either.
  }
}
