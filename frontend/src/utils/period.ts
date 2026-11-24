import { localDateKey, MS_PER_DAY, startOfLocalDay } from '@/utils/dates'
import { DAY_MONTH, formatDate } from '@/utils/format'

/**
 * A page's period: a preset that moves with the calendar, or two fixed dates. Every page that
 * filters by date offers the same presets, less the ones longer than it can load, and keeps its
 * own choice: a year of trips is fine to sum up but too much to draw on the map.
 */
export const PERIOD_PRESETS = [
  'last7',
  'last30',
  'last90',
  'thisMonth',
  'lastMonth',
  'thisYear',
  'lastYear',
  'custom',
] as const

export type PeriodPreset = (typeof PERIOD_PRESETS)[number]

export interface Period {
  preset: PeriodPreset
  /** The first and last day of a custom period, "YYYY-MM-DD"; null for a preset. */
  from: string | null
  to: string | null
}

/** A period as days on the calendar: the first and last local day, both included. */
export interface ResolvedPeriod {
  first: Date
  last: Date
  days: number
}

/** What a page offers: the presets it can load, the one it starts on, and its longest span. */
export interface PeriodOptions {
  presets: readonly PeriodPreset[]
  fallback: Period
  /** The most days a custom period may cover, as the page's API allows; none for no limit. */
  maxDays?: number
}

export function presetPeriod(preset: Exclude<PeriodPreset, 'custom'>): Period {
  return { preset, from: null, to: null }
}

/** The statistics sum days up, so they offer every preset and dates years apart. */
export const STATS_PERIOD: PeriodOptions = {
  presets: PERIOD_PRESETS,
  fallback: presetPeriod('last30'),
}

/** The map draws every trip's fixes, which its API allows for 90 days at most. */
export const MAP_PERIOD: PeriodOptions = {
  presets: ['last7', 'last30', 'last90', 'thisMonth', 'lastMonth', 'custom'],
  fallback: presetPeriod('last30'),
  maxDays: 90,
}

/** A car charges a few times a week, so the history starts on a longer period; at most 366 days. */
export const CHARGE_HISTORY_PERIOD: PeriodOptions = {
  presets: PERIOD_PRESETS,
  fallback: presetPeriod('last90'),
  maxDays: 366,
}

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/

/** A "YYYY-MM-DD" as that local day, or null when it is not one. */
export function parseLocalDate(text: string | null | undefined): Date | null {
  if (!text || !ISO_DATE.test(text)) return null
  const [y, m, d] = text.split('-').map(Number) as [number, number, number]
  const date = new Date(y, m - 1, d)
  return date.getFullYear() === y && date.getMonth() === m - 1 && date.getDate() === d ? date : null
}

export function daysBetween(first: Date, last: Date): number {
  // Rounded: a day across a summer-time change is 23 or 25 hours long.
  return (
    Math.round((startOfLocalDay(last).getTime() - startOfLocalDay(first).getTime()) / MS_PER_DAY) +
    1
  )
}

/** The local day `days` days after `day`, or before it when negative. */
export function addDays(day: Date, days: number): Date {
  return new Date(day.getFullYear(), day.getMonth(), day.getDate() + days)
}

function presetRange(preset: Exclude<PeriodPreset, 'custom'>, today: Date): [Date, Date] {
  const y = today.getFullYear()
  const m = today.getMonth()
  switch (preset) {
    case 'last7':
    case 'last30':
    case 'last90': {
      const back = { last7: 7, last30: 30, last90: 90 }[preset]
      return [new Date(y, m, today.getDate() - back), today]
    }
    case 'thisMonth':
      return [new Date(y, m, 1), today]
    case 'lastMonth':
      return [new Date(y, m - 1, 1), new Date(y, m, 0)]
    case 'thisYear':
      return [new Date(y, 0, 1), today]
    case 'lastYear':
      return [new Date(y - 1, 0, 1), new Date(y - 1, 11, 31)]
  }
}

/**
 * The days a period stands for on `now`. A custom period that is unreadable falls back to the
 * last 30 days; one given back to front is turned round; one reaching past today stops there;
 * one longer than `maxDays` keeps its last day and loses days from its start.
 */
export function resolvePeriod(
  period: Period,
  now: Date = new Date(),
  maxDays?: number,
): ResolvedPeriod {
  const today = startOfLocalDay(now)
  let first: Date
  let last: Date

  if (period.preset === 'custom') {
    const from = parseLocalDate(period.from)
    const to = parseLocalDate(period.to)
    if (from && to) [first, last] = from <= to ? [from, to] : [to, from]
    else [first, last] = presetRange('last30', today)
  } else {
    ;[first, last] = presetRange(period.preset, today)
  }

  if (last > today) last = today
  if (first > last) first = last
  if (period.preset === 'custom' && maxDays && daysBetween(first, last) > maxDays) {
    first = addDays(last, 1 - maxDays)
  }

  return { first, last, days: daysBetween(first, last) }
}

/**
 * What the API is asked for: the days as dates for the daily statistics, and as instants for the
 * trips and sessions. A period ending today asks up to now rather than up to midnight, so the
 * trip being driven counts as it does elsewhere.
 */
export function periodQuery(period: ResolvedPeriod, now: Date = new Date()) {
  const endsToday = localDateKey(period.last) === localDateKey(now)
  return {
    fromDate: localDateKey(period.first),
    toDate: localDateKey(period.last),
    fromIso: period.first.toISOString(),
    toIso: endsToday ? undefined : addDays(period.last, 1).toISOString(),
  }
}

/** Whether an instant falls in the period, as the API would read it. */
export function periodIncludes(period: ResolvedPeriod, instant: Date, now = new Date()): boolean {
  const { fromIso, toIso } = periodQuery(period, now)
  return instant >= new Date(fromIso) && (toIso === undefined || instant < new Date(toIso))
}

/** The period's days as a page shows them under its title: "6 Oct - 13 Oct 2026". */
export function formatPeriodRange({ first, last }: ResolvedPeriod): string {
  const sameYear = first.getFullYear() === last.getFullYear()
  const from = formatDate(first, {
    day: 'numeric',
    month: 'short',
    ...(sameYear ? {} : { year: 'numeric' }),
  })
  return `${from} - ${formatDate(last, { ...DAY_MONTH, year: 'numeric' })}`
}

export function isSamePeriod(a: Period, b: Period): boolean {
  return a.preset === b.preset && a.from === b.from && a.to === b.to
}

/**
 * A stored value as a period the page offers, or null when it is not one: localStorage and the
 * account's copy may hold what an older or newer build saved.
 */
export function parsePeriod(value: unknown, presets: readonly PeriodPreset[]): Period | null {
  if (typeof value !== 'object' || value === null) return null
  const v = value as Record<string, unknown>
  if (!(presets as readonly unknown[]).includes(v.preset)) return null
  const preset = v.preset as PeriodPreset
  if (preset !== 'custom') return presetPeriod(preset)
  const from = typeof v.from === 'string' ? v.from : null
  const to = typeof v.to === 'string' ? v.to : null
  return parseLocalDate(from) && parseLocalDate(to) ? { preset, from, to } : null
}
