import type { Place } from '@/services/mapApi'
import { TRIP_PURPOSES, type TripLogEntry, type TripPurpose } from '@/services/tripLogApi'
import { coordinateLabel, postalAddressLabel } from '@/utils/places'
import { pad2 } from '@/utils/dates'

/**
 * The rules of the trip log that do not need a component: which dates a period covers, which
 * distance a trip counts for, and how the totals add up. Kept here so the page and the CSV
 * export cannot disagree about any of them.
 */

/** A calendar month (0 to 11) of a year, or the whole year when `month` is null. */
export interface TripLogPeriod {
  year: number
  month: number | null
}

/**
 * The period's bounds, start included and end excluded, in the browser's own time zone: a
 * driver's September runs from their midnight to their midnight, not UTC's.
 */
export function periodRange({ year, month }: TripLogPeriod): { from: Date; to: Date } {
  return month === null
    ? { from: new Date(year, 0, 1), to: new Date(year + 1, 0, 1) }
    : { from: new Date(year, month, 1), to: new Date(year, month + 1, 1) }
}

/** "2026-09" for a month, "2026" for a year: sortable, and the same in every language. */
export function periodKey({ year, month }: TripLogPeriod): string {
  return month === null ? String(year) : `${year}-${pad2(month + 1)}`
}

/** The distance on the odometer, or null without both readings or when they run backwards. */
export function odometerDistanceKm(entry: TripLogEntry): number | null {
  const { odometerStartKm: start, odometerEndKm: end } = entry
  if (start === null || end === null || end < start) return null
  return end - start
}

/**
 * The distance the log counts a trip for: the odometer's when it has both readings, since GPS
 * fixes cut every corner, and the distance along the fixes otherwise.
 */
export function loggedDistanceKm(entry: TripLogEntry): number {
  return odometerDistanceKm(entry) ?? entry.distanceKm
}

export type PurposeKey = TripPurpose | 'unclassified'

/** Every purpose a trip can be counted under, with trips nobody classified yet last. */
export const PURPOSE_KEYS: readonly PurposeKey[] = [...TRIP_PURPOSES, 'unclassified']

/** One icon per purpose, shared by the totals and each trip's purpose buttons. */
export const PURPOSE_ICONS: Record<PurposeKey, string> = {
  business: 'briefcase',
  commute: 'building',
  private: 'house',
  unclassified: 'circle-question',
}

/** The badge a trip's purpose shows as in the list, coloured as its chip and its total. */
export const PURPOSE_BADGE_CLASSES: Record<PurposeKey, string> = {
  business: 'badge trip-log-badge--business',
  commute: 'badge badge-info',
  private: 'badge badge-success',
  unclassified: 'badge badge-warning',
}

/** The status card variant each purpose's total takes, matching its badge. */
export const PURPOSE_CARD_VARIANTS: Record<PurposeKey, 'info' | 'success' | 'warning' | undefined> =
  {
    business: undefined,
    commute: 'info',
    private: 'success',
    unclassified: 'warning',
  }

export interface PurposeTotal {
  trips: number
  km: number
}

export function purposeTotals(entries: readonly TripLogEntry[]): Record<PurposeKey, PurposeTotal> {
  const totals = Object.fromEntries(
    PURPOSE_KEYS.map((key) => [key, { trips: 0, km: 0 }]),
  ) as Record<PurposeKey, PurposeTotal>
  for (const entry of entries) {
    const total = totals[entry.purpose ?? 'unclassified']
    total.trips += 1
    total.km += loggedDistanceKm(entry)
  }
  return totals
}

/** How long the trip took, from leaving to arriving; never negative. */
export function tripDurationMs(entry: TripLogEntry): number {
  return Math.max(0, Date.parse(entry.endedAt) - Date.parse(entry.startedAt))
}

/** What the period's trips add up to, all purposes together. */
export interface PeriodSummary {
  trips: number
  km: number
  drivingMs: number
  /** Null without trips, as are the two below. */
  averageTripKm: number | null
  longestTripKm: number | null
  /** The distance over the time spent driving it, stops at lights and in traffic included. */
  averageSpeedKmh: number | null
}

export function periodSummary(entries: readonly TripLogEntry[]): PeriodSummary {
  let km = 0
  let drivingMs = 0
  let longest: number | null = null
  for (const entry of entries) {
    const distance = loggedDistanceKm(entry)
    km += distance
    drivingMs += tripDurationMs(entry)
    longest = longest === null ? distance : Math.max(longest, distance)
  }
  const trips = entries.length
  return {
    trips,
    km,
    drivingMs,
    averageTripKm: trips > 0 ? km / trips : null,
    longestTripKm: longest,
    averageSpeedKmh: drivingMs > 0 ? km / (drivingMs / 3_600_000) : null,
  }
}

/** The part of the period's distance one purpose takes, from 0 to 1; 0 when nothing was driven. */
export function distanceShare(total: PurposeTotal, allKm: number): number {
  return allKm > 0 ? Math.min(1, total.km / allKm) : 0
}

/**
 * How one end of a trip reads in the log: its address when known, its coordinates otherwise, so
 * a line never has a blank where the driver left or arrived.
 */
export function endLabel(place: Place | null, lat: number, lng: number): string {
  return postalAddressLabel(place) ?? coordinateLabel(lat, lng)
}

/** True while either end has not been looked up. An end with nothing mapped there counts as looked up. */
export function missingPlaces(entry: TripLogEntry): boolean {
  return entry.startPlace === null || entry.endPlace === null
}
