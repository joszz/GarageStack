import { distanceKm } from './geo'
import type { PhonePosition } from '@/services/phonePositionsApi'

/**
 * The pieces of recording phone GPS that hold no browser API of their own: which fixes are worth
 * sending, the id this device sends them under, and the queue that keeps them through a dead spot.
 */

const TRACKER_KEY = 'garagestack-phone-tracker'
const QUEUE_KEY = 'garagestack-phone-queue'

/** Worse fixes are ignored by the trip fusion anyway, so they are not sent at all. */
export const MAX_ACCURACY_M = 50
/** Closer together than this, fixes add nothing the fusion can use. */
export const MIN_INTERVAL_MS = 5_000
/** A phone that moved less is standing still: one fix a minute says so. */
export const MIN_DISTANCE_M = 10
export const STATIONARY_INTERVAL_MS = 60_000
/** About eight hours of driving at one fix every five seconds, before the oldest go. */
export const MAX_QUEUED = 6_000

const TRACKER_PATTERN = /^[A-Za-z0-9-]{8,64}$/

/**
 * The id this device sends its positions under, made once and kept. Random, so it says nothing
 * about the device: it only tells one phone's positions from another's.
 */
export function phoneTrackerId(): string {
  try {
    const stored = localStorage.getItem(TRACKER_KEY)
    if (stored && TRACKER_PATTERN.test(stored)) return stored
  } catch {
    // Storage refused: a fresh id for this visit is still a valid one.
  }
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  const id = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')
  try {
    localStorage.setItem(TRACKER_KEY, id)
  } catch {
    // As above.
  }
  return id
}

/** A fix as the recorder last kept it. */
export interface KeptFix {
  time: number
  latitude: number
  longitude: number
}

/** Whether a new fix is worth sending after the last one kept. */
export function worthKeeping(last: KeptFix | null, next: KeptFix, accuracyM: number): boolean {
  if (!(accuracyM <= MAX_ACCURACY_M)) return false
  if (!last) return true
  const elapsed = next.time - last.time
  if (elapsed < MIN_INTERVAL_MS) return false
  const metres = distanceKm([last.latitude, last.longitude], [next.latitude, next.longitude]) * 1000
  return metres >= MIN_DISTANCE_M || elapsed >= STATIONARY_INTERVAL_MS
}

/** The position the API takes, from what the browser reports. Speed comes in metres a second. */
export function toPhonePosition(position: GeolocationPosition): PhonePosition {
  const { latitude, longitude, accuracy, speed } = position.coords
  return {
    recordedAt: new Date(position.timestamp).toISOString(),
    latitude,
    longitude,
    accuracyM: accuracy,
    speedKmh: speed === null || !Number.isFinite(speed) || speed < 0 ? null : speed * 3.6,
  }
}

type Queue = Record<string, PhonePosition[]>

function isPosition(value: unknown): value is PhonePosition {
  if (typeof value !== 'object' || value === null) return false
  const p = value as Record<string, unknown>
  return (
    typeof p.recordedAt === 'string' &&
    typeof p.latitude === 'number' &&
    typeof p.longitude === 'number'
  )
}

function readQueue(): Queue {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(QUEUE_KEY) ?? '{}')
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return {}
    return Object.fromEntries(
      Object.entries(parsed).map(([vin, list]) => [
        vin,
        Array.isArray(list) ? list.filter(isPosition) : [],
      ]),
    )
  } catch {
    return {}
  }
}

function writeQueue(queue: Queue) {
  const kept = Object.fromEntries(Object.entries(queue).filter(([, list]) => list.length > 0))
  try {
    if (Object.keys(kept).length === 0) localStorage.removeItem(QUEUE_KEY)
    else localStorage.setItem(QUEUE_KEY, JSON.stringify(kept))
  } catch {
    // A full storage loses what is queued when the page closes, nothing more.
  }
}

/** The positions waiting to be sent for a car, oldest first. */
export function queuedPositions(vin: string): PhonePosition[] {
  return readQueue()[vin] ?? []
}

/** Adds a position for a car, dropping the oldest once the queue is full. */
export function queuePosition(vin: string, position: PhonePosition) {
  const queue = readQueue()
  queue[vin] = [...(queue[vin] ?? []), position].slice(-MAX_QUEUED)
  writeQueue(queue)
}

/**
 * Takes positions off a car's queue once they were sent, or refused for good. By their moment,
 * since positions recorded while a batch was on its way joined the queue meanwhile.
 */
export function dropQueued(vin: string, sent: readonly PhonePosition[]) {
  const moments = new Set(sent.map((p) => p.recordedAt))
  const queue = readQueue()
  queue[vin] = (queue[vin] ?? []).filter((p) => !moments.has(p.recordedAt))
  writeQueue(queue)
}

/** Forgotten on sign-out with the rest of the session: they are where the phone has been. */
export function forgetQueuedPositions() {
  try {
    localStorage.removeItem(QUEUE_KEY)
  } catch {
    // Nothing stored, then.
  }
}
