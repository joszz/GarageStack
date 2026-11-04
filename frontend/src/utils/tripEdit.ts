import type { TripPoint } from '@/services/vehicleApi'
import type { TripPointInput, TripStop } from '@/services/tripLogApi'
import { distanceKm, type LatLng } from '@/utils/geo'

/**
 * The route editor's operations on a trip's points, kept free of Leaflet so they can be tested on
 * their own. Each one returns a new list and leaves the one it was given alone, which is what
 * makes the editor's undo a stack of lists.
 *
 * Only positions are edited. A point the car reported keeps its time; a point someone places gets
 * one between its neighbours. The car's own fixes are never moved: moving the start or the end
 * past one adds a point in front of it or after it instead, and a fix can only be removed.
 */

/** Mirrors TripAnchor.AssumedSpeedKmh: how fast a stretch someone adds is taken to be driven. */
export const ASSUMED_SPEED_KMH = 25

/** Mirrors the segmenter's drift: a stop this close to an end is where the trip starts or ends already. */
export const SAME_PLACE_KM = 0.05

/** The times the trip's points must stay within: the trip before's end, and the next one's start. */
export interface TripLimits {
  notBefore: string | null
  notAfter: string | null
}

/** A trip has a start and an end at least, so no removal goes below this. */
export const MIN_POINTS = 2

const latLng = (p: TripPoint): LatLng => [p.latitude, p.longitude]

/** Whether the car reported this point while driving, as opposed to one someone or anchoring placed. */
export function isCarFix(point: TripPoint): boolean {
  return point.source == null
}

/** The length of a route along its points, in kilometres. */
export function routeKm(points: readonly TripPoint[]): number {
  let total = 0
  for (let i = 1; i < points.length; i++)
    total += distanceKm(latLng(points[i - 1]!), latLng(points[i]!))
  return total
}

/** Whether two routes are the same points in the same places at the same times. */
export function sameRoute(a: readonly TripPoint[], b: readonly TripPoint[]): boolean {
  return (
    a.length === b.length &&
    a.every((p, i) => {
      const q = b[i]!
      return (
        p.recordedAt === q.recordedAt && p.latitude === q.latitude && p.longitude === q.longitude
      )
    })
  )
}

/** The route as the server takes it: where and when, nothing it decides itself. */
export function toInput(points: readonly TripPoint[]): TripPointInput[] {
  return points.map(({ recordedAt, latitude, longitude }) => ({ recordedAt, latitude, longitude }))
}

function placed(latitude: number, longitude: number, at: number): TripPoint {
  return {
    recordedAt: new Date(at).toISOString(),
    latitude,
    longitude,
    speed: null,
    source: 'manual',
  }
}

// How long the car takes from one place to the other at the assumed speed.
function travelMs(from: LatLng, to: LatLng): number {
  return (distanceKm(from, to) / ASSUMED_SPEED_KMH) * 3_600_000
}

function clamp(at: number, earliest: string | null, latest: string | null): number {
  if (earliest !== null) at = Math.max(at, Date.parse(earliest))
  if (latest !== null) at = Math.min(at, Date.parse(latest))
  return at
}

/**
 * The route starting at `latitude`, `longitude`. A first point someone or anchoring placed is
 * moved there; a first point the car reported stays, with a new point in front of it. It leaves
 * `at` when given (a parking spot's suggested time), otherwise as long before the point after it
 * as the stretch takes at town speed, but never before the trip before it ended.
 */
export function withStart(
  points: readonly TripPoint[],
  latitude: number,
  longitude: number,
  limits: TripLimits,
  at?: string,
): TripPoint[] {
  const rest = isCarFix(points[0]!) ? points : points.slice(1)
  const next = rest[0]!
  const nextAt = Date.parse(next.recordedAt)
  const time = at
    ? Date.parse(at)
    : clamp(
        nextAt - travelMs([latitude, longitude], latLng(next)),
        limits.notBefore,
        next.recordedAt,
      )
  return [placed(latitude, longitude, time), ...rest]
}

/** The route ending at `latitude`, `longitude`: {@link withStart} the other way round. */
export function withEnd(
  points: readonly TripPoint[],
  latitude: number,
  longitude: number,
  limits: TripLimits,
  at?: string,
): TripPoint[] {
  const rest = isCarFix(points[points.length - 1]!) ? points : points.slice(0, -1)
  const previous = rest[rest.length - 1]!
  const previousAt = Date.parse(previous.recordedAt)
  const time = at
    ? Date.parse(at)
    : clamp(
        previousAt + travelMs(latLng(previous), [latitude, longitude]),
        previous.recordedAt,
        limits.notAfter,
      )
  return [...rest, placed(latitude, longitude, time)]
}

/** The route starting at the parking spot before it, left at the time the server suggests. */
export function withPreviousStop(points: readonly TripPoint[], stop: TripStop, limits: TripLimits) {
  return withStart(points, stop.latitude, stop.longitude, limits, stop.suggestedAt)
}

/** The route ending at the parking spot after it, reached at the time the server suggests. */
export function withNextStop(points: readonly TripPoint[], stop: TripStop, limits: TripLimits) {
  return withEnd(points, stop.latitude, stop.longitude, limits, stop.suggestedAt)
}

/** Whether a stop is far enough from the end it would replace to be worth offering. */
export function stopIsElsewhere(point: TripPoint | undefined, stop: TripStop | null): boolean {
  return (
    point !== undefined &&
    stop !== null &&
    distanceKm(latLng(point), [stop.latitude, stop.longitude]) > SAME_PLACE_KM
  )
}

/**
 * Where along a segment a position lies, from 0 at its start to 1 at its end, and how far from it.
 * Flat geometry, scaled for the latitude, is exact enough at the scale of one segment.
 */
function nearestOnSegment(from: LatLng, to: LatLng, at: LatLng): { fraction: number; km: number } {
  const scale = Math.cos((at[0] * Math.PI) / 180)
  const dx = (to[1] - from[1]) * scale
  const dy = to[0] - from[0]
  const px = (at[1] - from[1]) * scale
  const py = at[0] - from[0]
  const lengthSquared = dx * dx + dy * dy
  const fraction =
    lengthSquared === 0 ? 0 : Math.min(1, Math.max(0, (px * dx + py * dy) / lengthSquared))
  const on: LatLng = [
    from[0] + (to[0] - from[0]) * fraction,
    from[1] + (to[1] - from[1]) * fraction,
  ]
  return { fraction, km: distanceKm(on, at) }
}

/**
 * The route with a point added at `latitude`, `longitude`, in the segment nearest to it, at a
 * time as far between its neighbours' as it lies along the segment. Returns where it went too.
 */
export function withPointAdded(
  points: readonly TripPoint[],
  latitude: number,
  longitude: number,
): { points: TripPoint[]; index: number } {
  const at: LatLng = [latitude, longitude]
  let best = { segment: 0, fraction: 0.5, km: Infinity }
  for (let i = 0; i < points.length - 1; i++) {
    const { fraction, km } = nearestOnSegment(latLng(points[i]!), latLng(points[i + 1]!), at)
    if (km < best.km) best = { segment: i, fraction, km }
  }

  const from = Date.parse(points[best.segment]!.recordedAt)
  const to = Date.parse(points[best.segment + 1]!.recordedAt)
  // Never on a neighbour's own time: a point somewhere else at the same moment is no drive.
  const fraction = Math.min(0.99, Math.max(0.01, best.fraction))
  const index = best.segment + 1
  return {
    points: [
      ...points.slice(0, index),
      placed(latitude, longitude, Math.round(from + (to - from) * fraction)),
      ...points.slice(index),
    ],
    index,
  }
}

/** The route without the point at `index`, or the route as it was when that would leave too few. */
export function withPointRemoved(points: readonly TripPoint[], index: number): TripPoint[] {
  if (points.length <= MIN_POINTS || index < 0 || index >= points.length) return [...points]
  return points.filter((_, i) => i !== index)
}
