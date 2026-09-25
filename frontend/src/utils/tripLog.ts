import type { Place } from '@/services/mapApi'
import { TRIP_PURPOSES, type TripLogEntry, type TripPurpose } from '@/services/tripLogApi'
import { coordinateLabel, postalAddressLabel } from '@/utils/places'

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
  return month === null ? String(year) : `${year}-${String(month + 1).padStart(2, '0')}`
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
