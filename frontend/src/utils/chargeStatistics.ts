import type { ChargeSession } from '@/services/vehicleApi'

/**
 * Two charges one after the other, and what happened between them: how far the car was driven (the
 * odometer at the end of the older one to the odometer at the end of the newer one, which a charge
 * does not move) and how much of the battery that took (the level the older ended at, less the
 * level the newer started from).
 */
export interface ChargePair {
  distanceKm: number
  /** Null when either level is missing, and for a level that went up, which is not a drive. */
  socUsedPercent: number | null
}

/** A hop shorter than this between two charges is a charge that was split, not a drive. */
export const MIN_DISTANCE_BETWEEN_CHARGES_KM = 5

/** Below these the odometer's whole kilometres and the level's rounding are most of the figure. */
export const MIN_RANGE_PAIR_DISTANCE_KM = 10
export const MIN_RANGE_PAIR_SOC_USED = 10

/**
 * How many charge pairs a figure needs before it is shown: one or two are an anecdote, and a
 * single charge the car did not report would be most of it.
 */
export const MIN_PAIRS = 3

/**
 * How far from the middle one pair's kilometres per percent may be and still count. A charge no
 * poll caught between two reported ones gives a pair that spans two charges, with the battery
 * having gained in the middle, so its kilometres per percent is far above the rest; the pair that
 * far out is left out.
 */
export const RANGE_OUTLIER_FACTOR = 2

function finite(value: number | null): value is number {
  return value !== null && Number.isFinite(value)
}

function median(values: number[]): number {
  const sorted = [...values].sort((a, b) => a - b)
  const middle = Math.floor(sorted.length / 2)
  return sorted.length % 2 === 1 ? sorted[middle]! : (sorted[middle - 1]! + sorted[middle]!) / 2
}

/**
 * Each charge and the next one after it, oldest first. A charge still going has no end yet and
 * starts no pair; the one after it does not either, as the car is not where it ended.
 */
export function chargePairs(sessions: ChargeSession[]): ChargePair[] {
  const ordered = [...sessions].sort((a, b) => a.startedAt.localeCompare(b.startedAt))
  const pairs: ChargePair[] = []
  for (let i = 0; i < ordered.length - 1; i++) {
    const older = ordered[i]!
    const newer = ordered[i + 1]!
    if (older.endedAt === null || !finite(older.odometerKm) || !finite(newer.odometerKm)) continue
    const distanceKm = newer.odometerKm - older.odometerKm
    if (distanceKm < 0) continue

    const used =
      finite(older.endSocPercent) && finite(newer.startSocPercent)
        ? older.endSocPercent - newer.startSocPercent
        : null
    pairs.push({ distanceKm, socUsedPercent: used !== null && used > 0 ? used : null })
  }
  return pairs
}

/**
 * The distance a car typically goes from one charge to the next: the middle of the pairs, so that
 * a charge nobody saw, which makes one long pair, does not drag it up. Null with too few pairs.
 */
export function typicalDistanceBetweenCharges(pairs: ChargePair[]): number | null {
  const distances = pairs
    .map((p) => p.distanceKm)
    .filter((km) => km >= MIN_DISTANCE_BETWEEN_CHARGES_KM)
  return distances.length >= MIN_PAIRS ? median(distances) : null
}

/**
 * How far the car would go on a full battery at the way it was driven: the kilometres it covered
 * per percent of battery between charges, times 100. Only pairs long enough to read are used, the
 * ones far off the middle are left out (see {@link RANGE_OUTLIER_FACTOR}), and what is left is
 * added up rather than averaged, so a long stretch counts for more than a short one. Null with too
 * few pairs.
 */
export function fullBatteryRangeKm(pairs: ChargePair[]): number | null {
  const usable = pairs.filter(
    (p): p is ChargePair & { socUsedPercent: number } =>
      p.socUsedPercent !== null &&
      p.socUsedPercent >= MIN_RANGE_PAIR_SOC_USED &&
      p.distanceKm >= MIN_RANGE_PAIR_DISTANCE_KM,
  )
  if (usable.length < MIN_PAIRS) return null

  const middle = median(usable.map((p) => p.distanceKm / p.socUsedPercent))
  const kept = usable.filter((p) => {
    const perPercent = p.distanceKm / p.socUsedPercent
    return (
      perPercent >= middle / RANGE_OUTLIER_FACTOR && perPercent <= middle * RANGE_OUTLIER_FACTOR
    )
  })
  if (kept.length < MIN_PAIRS) return null

  const distance = kept.reduce((sum, p) => sum + p.distanceKm, 0)
  const used = kept.reduce((sum, p) => sum + p.socUsedPercent, 0)
  return (distance / used) * 100
}

/** The levels charges usually start from and end at. */
export interface ChargeHabit {
  startPercent: number
  endPercent: number
}

/** The average level the finished charges started at and ended at; null with too few of them. */
export function chargeHabit(sessions: ChargeSession[]): ChargeHabit | null {
  const levels = sessions.filter(
    (s) => s.endedAt !== null && finite(s.startSocPercent) && finite(s.endSocPercent),
  )
  if (levels.length < MIN_PAIRS) return null

  const average = (pick: (s: ChargeSession) => number | null) =>
    levels.reduce((sum, s) => sum + (pick(s) ?? 0), 0) / levels.length
  return {
    startPercent: average((s) => s.startSocPercent),
    endPercent: average((s) => s.endSocPercent),
  }
}
