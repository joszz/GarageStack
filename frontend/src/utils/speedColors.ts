import type { TripMatch } from '@/composables/useTripMatch'
import type { Trip } from '@/services/vehicleApi'
import { downsample } from '@/utils/downsample'

// The colours trips take on the map and in the trip list, in turn. The list's dots use the
// matching trip-list__dot--<n> classes, so the two stay in step.
const TRIP_COLORS = ['#3b82f6', '#10b981', '#f59e0b', '#ef4444', '#8b5cf6', '#ec4899']

export function tripColor(index: number): string {
  return TRIP_COLORS[index % TRIP_COLORS.length] ?? '#3b82f6'
}

export function tripColorClass(index: number): string {
  return `trip-list__dot--${index % TRIP_COLORS.length}`
}

// The speed overlay's scale: green when slow, through yellow and orange, to red at motorway
// speeds and above, blended in between.
const SPEED_STOPS: { speed: number; r: number; g: number; b: number }[] = [
  { speed: 0, r: 16, g: 185, b: 129 },
  { speed: 50, r: 132, g: 204, b: 22 },
  { speed: 90, r: 245, g: 158, b: 11 },
  { speed: 120, r: 249, g: 115, b: 22 },
  { speed: 150, r: 239, g: 68, b: 68 },
]

/** The overlay's colour for a speed in km/h, or `fallback` when the fix has no speed. */
export function speedToColor(speed: number | null, fallback: string): string {
  if (speed === null) return fallback
  const s = Math.max(0, speed)
  const last = SPEED_STOPS[SPEED_STOPS.length - 1]!
  if (s >= last.speed) return `rgb(${last.r},${last.g},${last.b})`
  let lo = SPEED_STOPS[0]!
  let hi = last
  for (let i = 0; i < SPEED_STOPS.length - 1; i++) {
    if (s >= SPEED_STOPS[i]!.speed && s < SPEED_STOPS[i + 1]!.speed) {
      lo = SPEED_STOPS[i]!
      hi = SPEED_STOPS[i + 1]!
      break
    }
  }
  const t = (s - lo.speed) / (hi.speed - lo.speed)
  const r = Math.round(lo.r + t * (hi.r - lo.r))
  const g = Math.round(lo.g + t * (hi.g - lo.g))
  const b = Math.round(lo.b + t * (hi.b - lo.b))
  return `rgb(${r},${g},${b})`
}

// Speed overlay renders one Leaflet polyline layer per segment. A long trip can have thousands
// of GPS points, which would create thousands of DOM elements - downsample first so the map
// stays responsive; a few hundred segments is already more color resolution than is visible.
export const MAX_SPEED_OVERLAY_SEGMENTS = 500

export interface SpeedSegment {
  coordinates: [number, number][]
  speed: number | null
}

/**
 * The stretches the speed overlay colours. Without a snapped line that is one straight stretch
 * per pair of fixes, as it always was; with one, each stretch follows the road between the two
 * fixes it spans, so the colours sit on the route that was driven rather than on the line cutting
 * across it.
 */
export function speedSegments(trip: Trip, match: TripMatch | null): SpeedSegment[] {
  const segments: SpeedSegment[] = []

  if (!match) {
    const pts = downsample(trip.points, MAX_SPEED_OVERLAY_SEGMENTS)
    for (let i = 0; i < pts.length - 1; i++) {
      const from = pts[i]!
      const to = pts[i + 1]!
      segments.push({
        coordinates: [
          [from.latitude, from.longitude],
          [to.latitude, to.longitude],
        ],
        speed: from.speed,
      })
    }
    return segments
  }

  // Pair every sent fix with its vertex on the snapped line before thinning, so a fix that
  // survives the thinning takes its place on that line with it.
  const placed = match.points.map((point, i) => ({ point, index: match.pointIndexes[i] ?? 0 }))
  const thinned = downsample(placed, MAX_SPEED_OVERLAY_SEGMENTS)

  let cursor = thinned[0]?.index ?? 0
  for (let i = 0; i < thinned.length - 1; i++) {
    const next = thinned[i + 1]!.index
    // Fixes the matcher could not place share a vertex with their neighbour. Carrying the cursor
    // past them keeps the coloured line continuous instead of leaving gaps in it.
    if (next <= cursor) continue
    segments.push({
      coordinates: match.coordinates.slice(cursor, next + 1),
      speed: thinned[i]!.point.speed,
    })
    cursor = next
  }

  // Whatever the snapped line runs on past the last fix still belongs to the trip.
  if (cursor < match.coordinates.length - 1) {
    segments.push({
      coordinates: match.coordinates.slice(cursor),
      speed: thinned[thinned.length - 1]?.point.speed ?? null,
    })
  }

  return segments
}
