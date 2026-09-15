import { describe, it, expect } from 'vitest'
import { daysAgoIso, startOfLocalDayDaysAgoIso } from '@/utils/dates'

describe('daysAgoIso', () => {
  it('subtracts whole days from the given instant', () => {
    const now = Date.UTC(2026, 8, 15, 12, 0, 0)
    expect(daysAgoIso(7, now)).toBe('2026-09-08T12:00:00.000Z')
  })
})

describe('startOfLocalDayDaysAgoIso', () => {
  it('lands on local midnight of the target day', () => {
    const now = new Date(2026, 8, 15, 17, 45, 0)
    const result = new Date(startOfLocalDayDaysAgoIso(30, now))
    expect(result.getFullYear()).toBe(2026)
    expect(result.getMonth()).toBe(7)
    expect(result.getDate()).toBe(16)
    expect(result.getHours()).toBe(0)
    expect(result.getMinutes()).toBe(0)
  })
})
