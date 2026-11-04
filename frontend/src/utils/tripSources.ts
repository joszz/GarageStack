import type { TripPoint } from '@/services/vehicleApi'
import type { LatLng } from '@/utils/geo'

/** A line drawn through a trip's points, with each point's vertex on it. */
export interface PlacedLine {
  coordinates: readonly LatLng[]
  points: readonly TripPoint[]
  /** One vertex index into `coordinates` per entry in `points`. */
  pointIndexes: readonly number[]
}

/**
 * The stretches of a trip's line that run to or from a point the car did not report while
 * driving (anchored to a parking spot, or placed by someone editing the route), as runs of
 * coordinates. The map draws them dashed: a straight line between two places, not a drive anyone
 * saw. Consecutive stretches join into one run.
 *
 * Without a `line` the stretches run straight between the points, as the raw trip is drawn; with
 * one (the snapped line), they follow it between the points' vertices.
 */
export function addedStretches(points: readonly TripPoint[], line?: PlacedLine | null): LatLng[][] {
  const fixes = line?.points ?? points
  const vertexOf = (i: number): number => line?.pointIndexes[i] ?? i
  const coordinates: readonly LatLng[] =
    line?.coordinates ?? points.map((p) => [p.latitude, p.longitude] as LatLng)

  const runs: LatLng[][] = []
  let run: LatLng[] | null = null
  let runEnd = -1
  for (let i = 0; i < fixes.length - 1; i++) {
    if (fixes[i]!.source == null && fixes[i + 1]!.source == null) {
      run = null
      continue
    }
    const from = vertexOf(i)
    const to = vertexOf(i + 1)
    if (to <= from) continue
    if (run && runEnd === from) {
      run.push(...coordinates.slice(from + 1, to + 1))
    } else {
      run = coordinates.slice(from, to + 1) as LatLng[]
      runs.push(run)
    }
    runEnd = to
  }
  return runs
}
