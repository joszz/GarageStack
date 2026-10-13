import { localDateKey } from '@/utils/dates'

/**
 * The statistics page's period: a preset that moves with the calendar, or two fixed dates. Kept
 * apart from the map's 7, 30 or 90 days, since a year of trips is fine to sum up but too much to
 * draw.
 */
export const STATS_PRESETS = [
  'last7',
  'last30',
  'last90',
  'thisMonth',
  'lastMonth',
  'thisYear',
  'lastYear',
  'custom',
] as const

export type StatsPreset = (typeof STATS_PRESETS)[number]

export interface StatsPeriod {
  preset: StatsPreset
  /** The first and last day of a custom period, "YYYY-MM-DD"; null for a preset. */
  from: string | null
  to: string | null
}

export const DEFAULT_STATS_PERIOD: StatsPeriod = { preset: 'last30', from: null, to: null }

/** How the charts group the days: per day up to about three months, per week up to a year, then per month. */
export type StatsGrouping = 'day' | 'week' | 'month'

/** A period as days on the calendar: the first and last local day, both included. */
export interface ResolvedStatsPeriod {
  first: Date
  last: Date
  days: number
  grouping: StatsGrouping
}

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/

function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate())
}

/** A "YYYY-MM-DD" as that local day, or null when it is not one. */
export function parseLocalDate(text: string | null | undefined): Date | null {
  if (!text || !ISO_DATE.test(text)) return null
  const [y, m, d] = text.split('-').map(Number) as [number, number, number]
  const date = new Date(y, m - 1, d)
  return date.getFullYear() === y && date.getMonth() === m - 1 && date.getDate() === d ? date : null
}

export function daysBetween(first: Date, last: Date): number {
  // Rounded: a day across a summer-time change is 23 or 25 hours long.
  return Math.round((startOfDay(last).getTime() - startOfDay(first).getTime()) / 86_400_000) + 1
}

export function groupingFor(days: number): StatsGrouping {
  if (days <= 92) return 'day'
  if (days <= 366) return 'week'
  return 'month'
}

function presetRange(preset: Exclude<StatsPreset, 'custom'>, today: Date): [Date, Date] {
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
 * last 30 days; one given back to front is turned round; one reaching past today stops there.
 */
export function resolveStatsPeriod(
  period: StatsPeriod,
  now: Date = new Date(),
): ResolvedStatsPeriod {
  const today = startOfDay(now)
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

  const days = daysBetween(first, last)
  return { first, last, days, grouping: groupingFor(days) }
}

/**
 * What the API is asked for: the days as dates for the daily statistics, and as instants for the
 * trips and climate figures. A period ending today asks up to now rather than up to midnight, so
 * the trip being driven counts as it does elsewhere.
 */
export function statsQuery(period: ResolvedStatsPeriod, now: Date = new Date()) {
  const endsToday = localDateKey(period.last) === localDateKey(now)
  const after = new Date(
    period.last.getFullYear(),
    period.last.getMonth(),
    period.last.getDate() + 1,
  )
  return {
    fromDate: localDateKey(period.first),
    toDate: localDateKey(period.last),
    fromIso: period.first.toISOString(),
    toIso: endsToday ? undefined : after.toISOString(),
  }
}

/** Whether a stored value is a period this build can read. */
export function isStatsPeriod(value: unknown): value is StatsPeriod {
  if (typeof value !== 'object' || value === null) return false
  const v = value as Record<string, unknown>
  if (!(STATS_PRESETS as readonly unknown[]).includes(v.preset)) return false
  if (v.preset !== 'custom') return true
  return parseLocalDate(v.from as string) !== null && parseLocalDate(v.to as string) !== null
}
