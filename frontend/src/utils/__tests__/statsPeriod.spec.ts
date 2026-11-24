import { describe, it, expect } from 'vitest'
import { groupingFor, resolveStatsPeriod } from '@/utils/statsPeriod'

// Tuesday 13 October 2026, mid-afternoon.
const NOW = new Date(2026, 9, 13, 15, 30)

describe('groupingFor', () => {
  it('charts per day up to 92 days, per week up to a year, then per month', () => {
    expect(groupingFor(92)).toBe('day')
    expect(groupingFor(93)).toBe('week')
    expect(groupingFor(366)).toBe('week')
    expect(groupingFor(367)).toBe('month')
  })
})

describe('resolveStatsPeriod', () => {
  it('groups the days of the period by its length', () => {
    const custom = (from: string, to: string) =>
      resolveStatsPeriod({ preset: 'custom', from, to }, NOW)

    expect(resolveStatsPeriod({ preset: 'thisMonth', from: null, to: null }, NOW)).toMatchObject({
      days: 13,
      grouping: 'day',
    })
    expect(custom('2026-01-01', '2026-10-13').grouping).toBe('week')
    expect(custom('2024-01-01', '2026-10-13').grouping).toBe('month')
  })
})
