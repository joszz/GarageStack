import type { DailyStat, TripSummary } from '@/services/vehicleApi'
import { localDateKey } from '@/utils/dates'
import { DAY_MONTH } from '@/utils/format'
import { parseLocalDate, type StatsGrouping } from '@/utils/statsPeriod'

/**
 * The trip figures the statistics page shows, read from trip summaries rather than from every
 * fix of the period. Each returns null when there are no trips to say anything about.
 */

export interface MapSpot {
  lat: number
  lng: number
}

/** Kilometres driven over all trips, unrounded: the unit formatter rounds once, in its own unit. */
export function totalDistanceKm(trips: readonly TripSummary[]): number | null {
  if (trips.length === 0) return null
  return trips.reduce((sum, trip) => sum + trip.distanceKm, 0)
}

export function averageTripKm(trips: readonly TripSummary[]): number | null {
  const total = totalDistanceKm(trips)
  return total === null ? null : total / trips.length
}

/** The local hour (0 to 23) most trips started in. On a tie, the hour an earlier trip started in wins. */
export function peakDriveHour(trips: readonly TripSummary[]): number | null {
  if (trips.length === 0) return null
  const counts = new Map<number, number>()
  for (const trip of trips) {
    const hour = new Date(trip.startedAt).getHours()
    counts.set(hour, (counts.get(hour) ?? 0) + 1)
  }
  let bestHour = 0
  let bestCount = 0
  for (const [hour, count] of counts) {
    if (count > bestCount) {
      bestHour = hour
      bestCount = count
    }
  }
  return bestHour
}

/**
 * Where the trips ended, one entry per spot: ends that agree to three decimals (about 100 m) are
 * the same place visited again.
 */
export function parkingSpots(trips: readonly TripSummary[]): MapSpot[] {
  const spots = new Map<string, MapSpot>()
  for (const trip of trips) {
    if (trip.endLatitude === null || trip.endLongitude === null) continue
    const key = `${trip.endLatitude.toFixed(3)},${trip.endLongitude.toFixed(3)}`
    if (!spots.has(key)) spots.set(key, { lat: trip.endLatitude, lng: trip.endLongitude })
  }
  return [...spots.values()]
}

/**
 * The average speed while moving over every trip, each weighed by how many readings its own
 * average covers, which equals the mean of all those readings together.
 */
export function averageMovingSpeedKmh(trips: readonly TripSummary[]): number | null {
  let weighted = 0
  let samples = 0
  for (const trip of trips) {
    if (trip.avgMovingSpeedKmh === null || trip.movingSpeedSamples === 0) continue
    weighted += trip.avgMovingSpeedKmh * trip.movingSpeedSamples
    samples += trip.movingSpeedSamples
  }
  return samples > 0 ? weighted / samples : null
}

/** Whether any trip reported a speed at all, which is what makes a speed figure worth showing. */
export function hasSpeedReadings(trips: readonly TripSummary[]): boolean {
  return trips.some((trip) => trip.maxSpeedKmh !== null)
}

// ── Days, weeks and months ──────────────────────────────────────────────────

/** The days a chart point stands for: one day, a week from Monday, or a calendar month. */
export interface StatsBucket {
  /** The day, the week's Monday or the month's first day, "YYYY-MM-DD". */
  key: string
  /** The first day of the period it covers: a week cut off by the period starts on that day. */
  start: Date
  days: DailyStat[]
}

function bucketStart(day: Date, grouping: StatsGrouping): Date {
  if (grouping === 'month') return new Date(day.getFullYear(), day.getMonth(), 1)
  if (grouping === 'week') {
    // Weeks start on Monday, as ISO weeks do.
    const sinceMonday = (day.getDay() + 6) % 7
    return new Date(day.getFullYear(), day.getMonth(), day.getDate() - sinceMonday)
  }
  return day
}

/** The period's days in chart points, oldest first. The API sends every day, so none is missing. */
export function bucketDays(stats: readonly DailyStat[], grouping: StatsGrouping): StatsBucket[] {
  const buckets = new Map<string, StatsBucket>()
  for (const stat of stats) {
    const day = parseLocalDate(stat.date)
    if (!day) continue
    const key = localDateKey(bucketStart(day, grouping))
    const bucket = buckets.get(key)
    if (bucket) bucket.days.push(stat)
    else buckets.set(key, { key, start: day, days: [stat] })
  }
  return [...buckets.values()]
}

function average(values: readonly (number | null)[]): number | null {
  const valid = values.filter((v): v is number => v !== null)
  if (!valid.length) return null
  return valid.reduce((sum, v) => sum + v, 0) / valid.length
}

/** Each point's average of a daily average, null for a point without any. */
export function bucketAverages(
  buckets: readonly StatsBucket[],
  read: (day: DailyStat) => number | null,
): (number | null)[] {
  return buckets.map((bucket) => average(bucket.days.map(read)))
}

/** Each point's sum of a daily total, null for a point without any. */
export function bucketTotals(
  buckets: readonly StatsBucket[],
  read: (day: DailyStat) => number | null,
): (number | null)[] {
  return buckets.map((bucket) => {
    const values = bucket.days.map(read).filter((v): v is number => v !== null)
    return values.length ? values.reduce((sum, v) => sum + v, 0) : null
  })
}

/**
 * How a chart's axis names its points: a day or a week by its first day ("10 Sep"), a month by
 * its name. The year is added only when the period runs into another one, where two "3 Jan" ticks
 * a year apart would read the same.
 */
export function bucketTickFormat(
  buckets: readonly StatsBucket[],
  grouping: StatsGrouping,
): Intl.DateTimeFormatOptions {
  const firstYear = buckets[0]?.start.getFullYear()
  const lastYear = buckets[buckets.length - 1]?.start.getFullYear()
  const year = firstYear === lastYear ? {} : { year: 'numeric' as const }
  return grouping === 'month' ? { month: 'short', ...year } : { ...DAY_MONTH, ...year }
}

/** Whether the car reported anything at all in the period. */
export function hasDailyReadings(stats: readonly DailyStat[]): boolean {
  return stats.some(
    (s) =>
      s.evSocPercent !== null ||
      s.fuelLevelPercent !== null ||
      s.batteryVoltage !== null ||
      s.counterTotal !== null ||
      s.tyrePressureFrontLeft !== null,
  )
}

export function roundTo2(value: number): number {
  return Math.round(value * 100) / 100
}

/**
 * How far the 12 V battery's daily average moved from the first day with a reading to the last,
 * in volts to two decimals. Null with fewer than two such days.
 */
export function batteryVoltageChange(stats: readonly DailyStat[]): number | null {
  const voltages = stats.map((s) => s.batteryVoltage).filter((v): v is number => v !== null)
  if (voltages.length < 2) return null
  return roundTo2(voltages[voltages.length - 1]! - voltages[0]!)
}

// ── Per month ───────────────────────────────────────────────────────────────

export interface MonthTotal {
  /** The month, "YYYY-MM". */
  key: string
  start: Date
  trips: number
  distanceKm: number
  /** The energy counter's total over the month, in its own unit; null when it reported none. */
  counterTotal: number | null
}

function monthKey(date: Date): string {
  return localDateKey(date).slice(0, 7)
}

/** Every month the period touches, oldest first, with its trips and its counter total. */
export function monthlyTotals(
  trips: readonly TripSummary[],
  stats: readonly DailyStat[],
  first: Date,
  last: Date,
): MonthTotal[] {
  const months = new Map<string, MonthTotal>()
  for (
    let month = new Date(first.getFullYear(), first.getMonth(), 1);
    month <= last;
    month = new Date(month.getFullYear(), month.getMonth() + 1, 1)
  ) {
    months.set(monthKey(month), {
      key: monthKey(month),
      start: month,
      trips: 0,
      distanceKm: 0,
      counterTotal: null,
    })
  }

  for (const trip of trips) {
    const month = months.get(monthKey(new Date(trip.startedAt)))
    if (!month) continue
    month.trips++
    month.distanceKm += trip.distanceKm
  }

  for (const stat of stats) {
    const month = months.get(stat.date.slice(0, 7))
    if (month && stat.counterTotal !== null)
      month.counterTotal = (month.counterTotal ?? 0) + stat.counterTotal
  }

  return [...months.values()]
}

/**
 * Widens a range outward to round numbers, stepping by half its order of magnitude: 1.5 to 3.5
 * bar stays as it is, and the same range reads 20 to 55 psi or 150 to 350 kPa rather than
 * starting the axis at 21.76.
 */
export function roundedRange(min: number, max: number): { min: number; max: number } {
  const step = 10 ** Math.floor(Math.log10(max - min)) / 2
  return { min: Math.floor(min / step) * step, max: Math.ceil(max / step) * step }
}
