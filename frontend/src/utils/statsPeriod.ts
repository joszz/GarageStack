import { resolvePeriod, type Period, type ResolvedPeriod } from '@/utils/period'

/** How the charts group the days: per day up to about three months, per week up to a year, then per month. */
export type StatsGrouping = 'day' | 'week' | 'month'

/** The statistics page's days, with how its charts group them. */
export interface ResolvedStatsPeriod extends ResolvedPeriod {
  grouping: StatsGrouping
}

export function groupingFor(days: number): StatsGrouping {
  if (days <= 92) return 'day'
  if (days <= 366) return 'week'
  return 'month'
}

export function resolveStatsPeriod(period: Period, now: Date = new Date()): ResolvedStatsPeriod {
  const resolved = resolvePeriod(period, now)
  return { ...resolved, grouping: groupingFor(resolved.days) }
}
