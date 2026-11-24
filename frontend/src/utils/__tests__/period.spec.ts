import { describe, it, expect } from 'vitest'
import {
  addDays,
  daysBetween,
  formatPeriodRange,
  isSamePeriod,
  parseLocalDate,
  parsePeriod,
  PERIOD_PRESETS,
  periodIncludes,
  periodQuery,
  resolvePeriod,
  type Period,
} from '@/utils/period'
import { localDateKey } from '@/utils/dates'

// Tuesday 13 October 2026, mid-afternoon.
const NOW = new Date(2026, 9, 13, 15, 30)

function preset(name: Period['preset']): Period {
  return { preset: name, from: null, to: null }
}

function custom(from: string | null, to: string | null): Period {
  return { preset: 'custom', from, to }
}

function shown(period: Period, maxDays?: number) {
  const resolved = resolvePeriod(period, NOW, maxDays)
  return {
    first: localDateKey(resolved.first),
    last: localDateKey(resolved.last),
    days: resolved.days,
  }
}

describe('resolvePeriod', () => {
  it('counts the last days back from today', () => {
    expect(shown(preset('last7'))).toEqual({
      first: '2026-10-06',
      last: '2026-10-13',
      days: 8,
    })
    expect(shown(preset('last90')).first).toBe('2026-07-15')
  })

  it('runs this month and this year up to today', () => {
    expect(shown(preset('thisMonth'))).toEqual({
      first: '2026-10-01',
      last: '2026-10-13',
      days: 13,
    })
    expect(shown(preset('thisYear'))).toEqual({
      first: '2026-01-01',
      last: '2026-10-13',
      days: 286,
    })
  })

  it('covers the whole of last month and last year', () => {
    expect(shown(preset('lastMonth'))).toEqual({
      first: '2026-09-01',
      last: '2026-09-30',
      days: 30,
    })
    expect(shown(preset('lastYear'))).toEqual({
      first: '2025-01-01',
      last: '2025-12-31',
      days: 365,
    })
  })

  it('finds last month across a new year', () => {
    const resolved = resolvePeriod(preset('lastMonth'), new Date(2027, 0, 5))

    expect(localDateKey(resolved.first)).toBe('2026-12-01')
    expect(localDateKey(resolved.last)).toBe('2026-12-31')
  })

  it('keeps a custom period longer than the page can load to its last days', () => {
    expect(shown(custom('2026-01-01', '2026-09-30'), 90)).toEqual({
      first: '2026-07-03',
      last: '2026-09-30',
      days: 90,
    })
    expect(shown(custom('2024-01-01', '2026-10-13'))).toMatchObject({ first: '2024-01-01' })
  })

  it('leaves a preset whole, whatever the limit', () => {
    expect(shown(preset('last90'), 90)).toEqual(shown(preset('last90')))
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

describe('daysBetween', () => {
  it('counts both ends, also across a change to winter time', () => {
    expect(daysBetween(new Date(2026, 9, 13), new Date(2026, 9, 13))).toBe(1)
    expect(daysBetween(new Date(2026, 9, 20), new Date(2026, 9, 30, 23))).toBe(11)
  })
})

describe('addDays', () => {
  it('moves by calendar days, across a month and a change to winter time', () => {
    expect(addDays(new Date(2026, 9, 31), 1)).toEqual(new Date(2026, 10, 1))
    expect(addDays(new Date(2026, 9, 20), 10)).toEqual(new Date(2026, 9, 30))
    expect(addDays(new Date(2026, 0, 1), -1)).toEqual(new Date(2025, 11, 31))
  })
})

describe('periodQuery', () => {
  it('asks up to now for a period that ends today', () => {
    const query = periodQuery(resolvePeriod(preset('thisMonth'), NOW), NOW)

    expect(query.fromDate).toBe('2026-10-01')
    expect(query.toDate).toBe('2026-10-13')
    expect(query.fromIso).toBe(new Date(2026, 9, 1).toISOString())
    expect(query.toIso).toBeUndefined()
  })

  it('asks up to the midnight after the last day of a period in the past', () => {
    const query = periodQuery(resolvePeriod(preset('lastMonth'), NOW), NOW)

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

describe('parsePeriod', () => {
  it('reads a known preset and a custom period with two dates', () => {
    expect(parsePeriod(preset('lastYear'), PERIOD_PRESETS)).toEqual(preset('lastYear'))
    expect(parsePeriod(custom('2026-01-01', '2026-03-31'), PERIOD_PRESETS)).toEqual(
      custom('2026-01-01', '2026-03-31'),
    )
  })

  it('drops the dates a preset was stored with', () => {
    expect(parsePeriod({ preset: 'last7', from: '2026-01-01', to: 'x' }, PERIOD_PRESETS)).toEqual(
      preset('last7'),
    )
  })

  it('refuses a preset the page does not offer', () => {
    expect(parsePeriod(preset('lastYear'), ['last7', 'last30'])).toBeNull()
  })

  it('refuses what an older or newer build may have stored', () => {
    expect(parsePeriod(null, PERIOD_PRESETS)).toBeNull()
    expect(parsePeriod({ preset: 'last365' }, PERIOD_PRESETS)).toBeNull()
    expect(parsePeriod(custom('2026-01-01', null), PERIOD_PRESETS)).toBeNull()
  })
})

describe('isSamePeriod', () => {
  it('compares the preset and both dates', () => {
    expect(isSamePeriod(preset('last30'), preset('last30'))).toBe(true)
    expect(isSamePeriod(preset('last30'), preset('last90'))).toBe(false)
    expect(
      isSamePeriod(custom('2026-01-01', '2026-02-01'), custom('2026-01-01', '2026-02-02')),
    ).toBe(false)
  })
})

describe('periodIncludes', () => {
  it('takes a period ending today up to now, and one in the past up to the midnight after it', () => {
    const thisMonth = resolvePeriod(preset('thisMonth'), NOW)
    expect(periodIncludes(thisMonth, new Date(2026, 9, 1), NOW)).toBe(true)
    expect(periodIncludes(thisMonth, new Date(2026, 8, 30, 23, 59), NOW)).toBe(false)

    const lastMonth = resolvePeriod(preset('lastMonth'), NOW)
    expect(periodIncludes(lastMonth, new Date(2026, 8, 30, 23, 59), NOW)).toBe(true)
    expect(periodIncludes(lastMonth, new Date(2026, 9, 1), NOW)).toBe(false)
  })
})

describe('formatPeriodRange', () => {
  it('names the year once within a year, and on both ends across one', () => {
    expect(formatPeriodRange(resolvePeriod(preset('thisMonth'), NOW))).toBe('Oct 1 - Oct 13, 2026')
    expect(formatPeriodRange(resolvePeriod(custom('2025-12-20', '2026-01-05'), NOW))).toBe(
      'Dec 20, 2025 - Jan 5, 2026',
    )
  })
})
