import type { TelemetryHistoryPoint, TripSummary } from '@/services/vehicleApi'
import { dailyCounterTotal } from '@/utils/energy'

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

/** The local hour most trips started in, as "HH:00". On a tie, the hour an earlier trip started in wins. */
export function peakDriveHour(trips: readonly TripSummary[]): string | null {
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
  return `${String(bestHour).padStart(2, '0')}:00`
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

// ── Daily history ───────────────────────────────────────────────────────────

/** A local calendar day of history readings, keyed "YYYY-MM-DD". */
export interface HistoryDay {
  key: string
  points: TelemetryHistoryPoint[]
}

export function localDateKey(date: Date): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

/**
 * The period's readings by local day, one entry for every day from `days` ago up to today even
 * when the car sent nothing that day, so a chart's x axis spans the whole period evenly.
 */
export function historyByDay(
  history: readonly TelemetryHistoryPoint[],
  days: number,
  now: Date = new Date(),
): HistoryDay[] {
  const buckets = new Map<string, TelemetryHistoryPoint[]>()

  const startDay = new Date(now)
  startDay.setDate(startDay.getDate() - days)
  for (
    let d = new Date(startDay.getFullYear(), startDay.getMonth(), startDay.getDate());
    d <= now;
    d.setDate(d.getDate() + 1)
  ) {
    buckets.set(localDateKey(d), [])
  }

  for (const point of history) {
    const key = localDateKey(new Date(point.recordedAt))
    const existing = buckets.get(key)
    if (existing) existing.push(point)
    else buckets.set(key, [point])
  }

  return Array.from(buckets.entries()).map(([key, points]) => ({ key, points }))
}

function average(values: readonly (number | null)[]): number | null {
  const valid = values.filter((v): v is number => v !== null)
  if (!valid.length) return null
  return valid.reduce((sum, v) => sum + v, 0) / valid.length
}

export function roundTo2(value: number): number {
  return Math.round(value * 100) / 100
}

/** Each day's average of one reading, null for a day without any. */
export function dailyAverages(
  days: readonly HistoryDay[],
  read: (point: TelemetryHistoryPoint) => number | null,
): (number | null)[] {
  return days.map((day) => average(day.points.map(read)))
}

/**
 * How far the 12 V battery's daily average moved from the first day with a reading to the last,
 * in volts to two decimals. Null with fewer than two such days.
 */
export function batteryVoltageChange(days: readonly HistoryDay[]): number | null {
  const averages = dailyAverages(days, (p) => p.batteryVoltage).filter(
    (v): v is number => v !== null,
  )
  if (averages.length < 2) return null
  return roundTo2(averages[averages.length - 1]! - averages[0]!)
}

/**
 * Each day's total from the car's daily energy counter, in whatever the counter holds (kWh on a
 * plug-in car, hundredths of a litre on a plain hybrid). Today is still running, which changes how
 * a counter reset has to be read.
 */
export function dailyCounterTotals(
  days: readonly HistoryDay[],
  now: Date = new Date(),
): (number | null)[] {
  const today = localDateKey(now)
  return days.map((day) => {
    const readings = day.points
      .slice()
      .sort((a, b) => new Date(a.recordedAt).getTime() - new Date(b.recordedAt).getTime())
      .map((p) => p.powerUsageOfDay)
      .filter((v): v is number => v !== null)
    return dailyCounterTotal(readings, day.key === today)
  })
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
