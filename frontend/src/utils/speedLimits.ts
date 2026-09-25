/**
 * What the speed readings along a snapped trip say against the limits on the roads it ran over.
 * The matcher answers with a limit per segment of the snapped line (from OSM's `maxspeed`), while
 * telemetry carries a speed per fix, so pairing the two means spreading each fix's reading over
 * the segments it covers and comparing there.
 *
 * Two things this cannot know, and which the UI says out loud rather than papering over:
 * OSM has no limit for a good share of roads, so part of every trip is simply unknown; and
 * conditional limits (a Dutch motorway signed 100 by day and 130 at night) are not in the tag
 * this reads, so a night drive there reads as over the limit.
 */
import { distanceKm } from './geo'

/**
 * How far above a limit a reading has to be before it counts as over it. Speedometers read high
 * by a few percent by design, and a fix is a spot sample rather than an average, so a small
 * margin keeps ordinary driving out of the "over" bucket.
 */
export const OVER_LIMIT_TOLERANCE_KPH = 5

export type SpeedLimitState = 'under' | 'over' | 'unknown'

/** The parts of a snapped trip this module needs; `TripMatch` satisfies it. */
export interface SnappedLine {
  /** The snapped line's vertices. */
  coordinates: [number, number][]
  /** The fixes that were sent, in order; only their speed reading matters here. */
  points: { speed: number | null }[]
  /** One vertex index into `coordinates` per entry in `points`. */
  pointIndexes: number[]
  /** One limit in km/h per segment of `coordinates`, null where no limit is known. */
  speedLimits: (number | null)[]
}

export interface SpeedLimitSummary {
  /** Length of the snapped line. */
  totalKm: number
  /** Of that, the distance where both a limit and a speed reading are known. */
  knownKm: number
  /** Of the known distance, the part driven above the limit by more than the tolerance. */
  overKm: number
  /** The largest amount any reading was over a limit by, rounded to whole km/h. */
  maxOverKph: number
  /** The limit that was exceeded by that much, for "27 over an 80". */
  maxOverLimitKph: number | null
}

/** One stretch of the line drawn as a single colour. */
export interface SpeedLimitSegment {
  coordinates: [number, number][]
  state: SpeedLimitState
}

/**
 * Unfolds the run-length pairs the API sends (segment count, then km/h) into one limit per
 * segment, with null where no limit is known. Runs that do not add up to `segmentCount` are
 * tolerated in both directions: the data comes from a cache row, and a line that draws with part
 * of its colouring missing beats one that does not draw at all.
 */
export function expandSpeedLimits(
  runs: number[] | null | undefined,
  segmentCount: number,
): (number | null)[] {
  const limits: (number | null)[] = Array.from({ length: Math.max(segmentCount, 0) }, () => null)
  if (!runs || segmentCount <= 0) return limits

  let at = 0
  for (let i = 0; i + 1 < runs.length && at < segmentCount; i += 2) {
    const count = runs[i]!
    const limit = runs[i + 1]!
    const until = Math.min(at + Math.max(count, 0), segmentCount)
    if (limit > 0) for (let s = at; s < until; s++) limits[s] = limit
    at = until
  }

  return limits
}

/**
 * The speed reading covering each segment of the line. A fix's reading holds until the next fix,
 * and the first fix's reading also covers whatever the line runs before it: that stretch is the
 * start of the trip, not a gap in it.
 */
function speedPerSegment(line: SnappedLine, segmentCount: number): (number | null)[] {
  const speeds: (number | null)[] = Array.from({ length: Math.max(segmentCount, 0) }, () => null)

  for (let fix = 0; fix < line.pointIndexes.length; fix++) {
    const from = fix === 0 ? 0 : (line.pointIndexes[fix] ?? 0)
    const to =
      fix + 1 < line.pointIndexes.length ? (line.pointIndexes[fix + 1] ?? from) : segmentCount
    const speed = line.points[fix]?.speed ?? null
    for (let s = Math.max(from, 0); s < Math.min(to, segmentCount); s++) speeds[s] = speed
  }

  return speeds
}

function stateOf(
  speed: number | null,
  limit: number | null,
  toleranceKph: number,
): SpeedLimitState {
  if (limit === null || speed === null) return 'unknown'
  return speed > limit + toleranceKph ? 'over' : 'under'
}

/** How the trip went against the limits, in kilometres rather than in readings. */
export function speedLimitSummary(
  line: SnappedLine,
  toleranceKph: number = OVER_LIMIT_TOLERANCE_KPH,
): SpeedLimitSummary {
  const segmentCount = Math.max(line.coordinates.length - 1, 0)
  const speeds = speedPerSegment(line, segmentCount)
  const summary: SpeedLimitSummary = {
    totalKm: 0,
    knownKm: 0,
    overKm: 0,
    maxOverKph: 0,
    maxOverLimitKph: null,
  }

  for (let s = 0; s < segmentCount; s++) {
    const km = distanceKm(line.coordinates[s]!, line.coordinates[s + 1]!)
    summary.totalKm += km

    const limit = line.speedLimits[s] ?? null
    const speed = speeds[s]
    const state = stateOf(speed ?? null, limit, toleranceKph)
    if (state === 'unknown') continue

    summary.knownKm += km
    if (state === 'over') {
      summary.overKm += km
      const over = speed! - limit!
      if (over > summary.maxOverKph) {
        summary.maxOverKph = over
        summary.maxOverLimitKph = limit
      }
    }
  }

  summary.maxOverKph = Math.round(summary.maxOverKph)
  return summary
}

/**
 * The line split into stretches of one colour each: consecutive segments in the same state become
 * one polyline, so a trip is a handful of layers rather than one per vertex.
 */
export function speedLimitSegments(
  line: SnappedLine,
  toleranceKph: number = OVER_LIMIT_TOLERANCE_KPH,
): SpeedLimitSegment[] {
  const segmentCount = Math.max(line.coordinates.length - 1, 0)
  if (segmentCount === 0) return []

  const speeds = speedPerSegment(line, segmentCount)
  const segments: SpeedLimitSegment[] = []
  let start = 0
  let current = stateOf(speeds[0] ?? null, line.speedLimits[0] ?? null, toleranceKph)

  for (let s = 1; s <= segmentCount; s++) {
    const state =
      s < segmentCount
        ? stateOf(speeds[s] ?? null, line.speedLimits[s] ?? null, toleranceKph)
        : null

    if (state === current) continue

    // A stretch runs from the first vertex of its first segment to the last of its final one, so
    // neighbouring stretches share a vertex and the line stays unbroken.
    segments.push({ coordinates: line.coordinates.slice(start, s + 1), state: current })
    start = s
    if (state !== null) current = state
  }

  return segments
}
