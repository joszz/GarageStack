/**
 * The points the heatmap is drawn from.
 *
 * Telemetry arrives on a clock rather than on a distance: a queue leaves dozens of fixes within a
 * few hundred metres while a motorway leaves one every kilometre. Feeding those fixes straight to
 * the heatmap therefore maps where the car stood still, with traffic jams glowing hottest. Taking
 * a sample every fixed distance along each trip's line instead makes every point stand for the
 * same stretch of road, so what the heat shows is how often a road was driven.
 */
import { polylineLengthKm, resampleByDistance, type LatLng } from './geo'
import { downsample } from './downsample'

/**
 * Samples closer together than this are finer than the heat radius can show and only cost frames
 * to draw, so a short history stops getting denser rather than sharper.
 */
const MIN_STEP_KM = 0.02

/**
 * A jump longer than this is the car having stopped reporting, not a straight stretch of road:
 * sampling across it would paint heat over whatever the line happens to cross.
 */
const MAX_GAP_KM = 5

/**
 * Evenly spaced samples along every line, together holding to `budget` points (give or take the
 * one extra `downsample` keeps so a line ends where it ended). The spacing follows from how far
 * the lines run in total, so a long history draws coarser rather than heavier: what the heat says
 * about one road against another does not depend on the period shown.
 */
export function distanceWeightedSamples(lines: readonly LatLng[][], budget: number): LatLng[] {
  if (budget <= 0) return []

  const totalKm = lines.reduce((sum, line) => sum + polylineLengthKm(line, MAX_GAP_KM), 0)
  const stepKm = Math.max(totalKm / budget, MIN_STEP_KM)
  const samples = lines.flatMap((line) => resampleByDistance(line, stepKm, MAX_GAP_KM))

  // Every line contributes its first coordinate whatever its length, so a great many very short
  // trips can still overshoot the budget the spacing was derived from.
  return downsample(samples, budget)
}
