/**
 * The geometry the map's overlays share: how far apart two coordinates are, how long a line is,
 * and how to walk a line taking a sample every so many kilometres. Kilometres throughout, which
 * is the unit the trip API and the rest of the map already speak.
 */

export type LatLng = [number, number]

const EARTH_RADIUS_KM = 6371

/** Great-circle distance between two coordinates. */
export function distanceKm(from: LatLng, to: LatLng): number {
  const toRad = Math.PI / 180
  const dLat = (to[0] - from[0]) * toRad
  const dLng = (to[1] - from[1]) * toRad
  const lat1 = from[0] * toRad
  const lat2 = to[0] * toRad
  const a = Math.sin(dLat / 2) ** 2 + Math.sin(dLng / 2) ** 2 * Math.cos(lat1) * Math.cos(lat2)
  return 2 * EARTH_RADIUS_KM * Math.asin(Math.min(1, Math.sqrt(a)))
}

/**
 * Length of a line, leaving out jumps longer than `maxGapKm`. Such a jump is the car having
 * stopped reporting for a while rather than a stretch it is known to have driven in a straight
 * line, so counting it would claim a distance nothing recorded.
 */
export function polylineLengthKm(line: readonly LatLng[], maxGapKm: number): number {
  let total = 0
  for (let i = 1; i < line.length; i++) {
    const segment = distanceKm(line[i - 1]!, line[i]!)
    if (segment <= maxGapKm) total += segment
  }
  return total
}

/** A coordinate a fraction of the way along a segment. Linear is exact enough within a few km. */
function interpolate(from: LatLng, to: LatLng, fraction: number): LatLng {
  return [from[0] + (to[0] - from[0]) * fraction, from[1] + (to[1] - from[1]) * fraction]
}

/**
 * Walks a line and returns a coordinate every `stepKm`, so each sample stands for the same
 * distance travelled rather than for the same amount of time. The first coordinate is always
 * returned, which keeps a line shorter than one step on the map instead of dropping it.
 *
 * A jump longer than `maxGapKm` is stepped over rather than sampled across: the fixes on either
 * side say the car was in both places, not which way it went between them.
 */
export function resampleByDistance(
  line: readonly LatLng[],
  stepKm: number,
  maxGapKm: number,
): LatLng[] {
  if (line.length === 0) return []

  const samples: LatLng[] = [line[0]!]
  if (line.length === 1 || !(stepKm > 0)) return samples

  // Distance walked since the last sample, carried across segment boundaries so that sampling is
  // even along the whole line rather than restarting at every vertex.
  let carried = 0

  for (let i = 1; i < line.length; i++) {
    const from = line[i - 1]!
    const to = line[i]!
    const segment = distanceKm(from, to)

    if (segment > maxGapKm) {
      samples.push(to)
      carried = 0
      continue
    }

    let offset = stepKm - carried
    while (offset <= segment) {
      samples.push(interpolate(from, to, offset / segment))
      offset += stepKm
    }
    carried = segment - (offset - stepKm)
  }

  return samples
}
