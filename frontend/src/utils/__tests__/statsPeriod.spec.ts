import { describe, it, expect } from 'vitest'
import {
  daysBetween,
  groupingFor,
  isStatsPeriod,
  parseLocalDate,
  resolveStatsPeriod,
  statsQuery,
  type StatsPeriod,
} from '@/utils/statsPeriod'
import { localDateKey } from '@/utils/dates'

// Tuesday 13 October 2026, mid-afternoon.
const NOW = new Date(2026, 9, 13, 15, 30)

function preset(name: StatsPeriod['preset']): StatsPeriod {
  return { preset: name, from: null, to: null }
}

function custom(from: string | null, to: string | null): StatsPeriod {
  return { preset: 'custom', from, to }
}

function shown(period: StatsPeriod) {
  const resolved = resolveStatsPeriod(period, NOW)
  return {
    first: localDateKey(resolved.first),
    last: localDateKey(resolved.last),
    days: resolved.days,
    grouping: resolved.grouping,
  }
}

describe('resolveStatsPeriod', () => {
  it('counts the last days back from today, like the map does', () => {
    expect(shown(preset('last7'))).toEqual({
      first: '2026-10-06',
      last: '2026-10-13',
      days: 8,
      grouping: 'day',
    })
    expect(shown(preset('last90')).first).toBe('2026-07-15')
  })

  it('runs this month and this year up to today', () => {
    expect(shown(preset('thisMonth'))).toEqual({
      first: '2026-10-01',
      last: '2026-10-13',
      days: 13,
      grouping: 'day',
    })
    expect(shown(preset('thisYear'))).toEqual({
      first: '2026-01-01',
      last: '2026-10-13',
      days: 286,
      grouping: 'week',
    })
  })

  it('covers the whole of last month and last year', () => {
    expect(shown(preset('lastMonth'))).toEqual({
      first: '2026-09-01',
      last: '2026-09-30',
      days: 30,
      grouping: 'day',
    })
    expect(shown(preset('lastYear'))).toEqual({
      first: '2025-01-01',
      last: '2025-12-31',
      days: 365,
      grouping: 'week',
    })
  })

  it('finds last month across a new year', () => {
    const resolved = resolveStatsPeriod(preset('lastMonth'), new Date(2027, 0, 5))

    expect(localDateKey(resolved.first)).toBe('2026-12-01')
    expect(localDateKey(resolved.last)).toBe('2026-12-31')
  })

  it('charts a custom period of years per month', () => {
    expect(shown(custom('2024-01-01', '2026-10-13'))).toMatchObject({ grouping: 'month' })
  })

  it('turns a custom period given back to front round', () => {
    expect(shown(custom('2026-09-30', '2026-09-01'))).toMatchObject({
      first: '2026-09-01',
      last: '2026-09-30',
    })
  })

  it('stops a custom period at today', () => {
    expect(shown(custom('2026-10-01', '2026-12-31'))).toMatchObject({
      first: '2026-10-01',
      last: '2026-10-13',
    })
    expect(shown(custom('2026-11-01', '2026-12-31'))).toMatchObject({
      first: '2026-10-13',
      last: '2026-10-13',
      days: 1,
    })
  })

  it('falls back to the last 30 days for a custom period it cannot read', () => {
    expect(shown(custom(null, '2026-10-01'))).toEqual(shown(preset('last30')))
    expect(shown(custom('2026-02-30', '2026-10-01'))).toEqual(shown(preset('last30')))
  })
})

describe('groupingFor', () => {
  it('charts per day up to 92 days, per week up to a year, then per month', () => {
    expect(groupingFor(92)).toBe('day')
    expect(groupingFor(93)).toBe('week')
    expect(groupingFor(366)).toBe('week')
    expect(groupingFor(367)).toBe('month')
  })
})

describe('daysBetween', () => {
  it('counts both ends, also across a change to winter time', () => {
    expect(daysBetween(new Date(2026, 9, 13), new Date(2026, 9, 13))).toBe(1)
    expect(daysBetween(new Date(2026, 9, 20), new Date(2026, 9, 30, 23))).toBe(11)
  })
})

describe('statsQuery', () => {
  it('asks up to now for a period that ends today', () => {
    const query = statsQuery(resolveStatsPeriod(preset('thisMonth'), NOW), NOW)

    expect(query.fromDate).toBe('2026-10-01')
    expect(query.toDate).toBe('2026-10-13')
    expect(query.fromIso).toBe(new Date(2026, 9, 1).toISOString())
    expect(query.toIso).toBeUndefined()
  })

  it('asks up to the midnight after the last day of a period in the past', () => {
    const query = statsQuery(resolveStatsPeriod(preset('lastMonth'), NOW), NOW)

    expect(query.toDate).toBe('2026-09-30')
    expect(query.toIso).toBe(new Date(2026, 9, 1).toISOString())
  })
})

describe('parseLocalDate', () => {
  it('reads a calendar day and nothing else', () => {
    expect(parseLocalDate('2026-10-13')).toEqual(new Date(2026, 9, 13))
    expect(parseLocalDate('2026-02-30')).toBeNull()
    expect(parseLocalDate('2026-2-3')).toBeNull()
    expect(parseLocalDate(null)).toBeNull()
  })
})

describe('isStatsPeriod', () => {
  it('accepts a known preset and a custom period with two dates', () => {
    expect(isStatsPeriod(preset('lastYear'))).toBe(true)
    expect(isStatsPeriod(custom('2026-01-01', '2026-03-31'))).toBe(true)
  })

  it('rejects what an older or newer build may have stored', () => {
    expect(isStatsPeriod(null)).toBe(false)
    expect(isStatsPeriod({ preset: 'last365' })).toBe(false)
    expect(isStatsPeriod(custom('2026-01-01', null))).toBe(false)
  })
})
