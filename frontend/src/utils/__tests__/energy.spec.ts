import { describe, it, expect } from 'vitest'
import { whPerKm, dailyEnergyKwh } from '@/utils/energy'

describe('whPerKm', () => {
  it('converts kWh over km to Wh/km', () => {
    // Issue #284: 16 kWh over 85 km on an MG IM5
    expect(whPerKm(16, 85)).toBeCloseTo(188.24, 2)
  })

  it('returns null without energy or distance', () => {
    expect(whPerKm(null, 10)).toBeNull()
    expect(whPerKm(5, null)).toBeNull()
  })

  it('returns null when no distance has been driven', () => {
    expect(whPerKm(0.4, 0)).toBeNull()
  })
})

describe('dailyEnergyKwh', () => {
  it('returns null without readings', () => {
    expect(dailyEnergyKwh([], false)).toBeNull()
    expect(dailyEnergyKwh([], true)).toBeNull()
  })

  it('uses the cumulative peak for a completed day', () => {
    expect(dailyEnergyKwh([2.1, 9.8, 16.3, 16.3], false)).toBe(16.3)
  })

  it('uses the peak after a counter reset on the current day', () => {
    // 21.4 kWh carried over from yesterday, reset after midnight, then driving resumed
    expect(dailyEnergyKwh([21.4, 0, 1.2, 4.5], true)).toBe(4.5)
  })

  it('treats a drop below 50 Wh as a reset even when the peak was small', () => {
    // 0.03 is above 5% of the 0.5 peak, so only the noise floor flags it as a reset
    expect(dailyEnergyKwh([0.5, 0.03, 0.8], true)).toBe(0.8)
  })

  it('returns null when the counter reset and nothing has been used since', () => {
    expect(dailyEnergyKwh([18.7, 0], true)).toBeNull()
  })

  it('returns only the net increase when the counter has not reset yet', () => {
    // A Wh-based floor (50) would flag every one of these as a reset and return 14
    expect(dailyEnergyKwh([10, 12.5, 14], true)).toBe(4)
  })

  it('returns null for an unchanged carryover with no driving', () => {
    expect(dailyEnergyKwh([7.3, 7.3], true)).toBeNull()
  })
})
