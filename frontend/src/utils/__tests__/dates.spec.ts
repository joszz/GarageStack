import { describe, it, expect } from 'vitest'
import { daysAgoIso, pad2, startOfLocalDay, timeOfDay } from '@/utils/dates'

describe('daysAgoIso', () => {
  it('subtracts whole days from the given instant', () => {
    const now = Date.UTC(2026, 8, 15, 12, 0, 0)
    expect(daysAgoIso(7, now)).toBe('2026-09-08T12:00:00.000Z')
  })
})

describe('timeOfDay', () => {
  it('reads a time with or without seconds as hours and minutes', () => {
    expect(timeOfDay('07:00')).toBe('07:00')
    expect(timeOfDay('23:45:00')).toBe('23:45')
  })

  it('reads nothing else', () => {
    expect(timeOfDay(null)).toBeNull()
    expect(timeOfDay('7:00')).toBeNull()
    expect(timeOfDay('24:00')).toBeNull()
  })
})

describe('pad2', () => {
  it('writes a number below ten with a leading zero', () => {
    expect([pad2(7), pad2(12)]).toEqual(['07', '12'])
  })
})

describe('startOfLocalDay', () => {
  it('is local midnight of the same day', () => {
    expect(startOfLocalDay(new Date(2026, 8, 15, 17, 45))).toEqual(new Date(2026, 8, 15))
  })
})
