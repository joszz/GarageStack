import { describe, it, expect } from 'vitest'
import { evLevelVariant, fuelLevelVariant } from '@/utils/levels'

describe('fuelLevelVariant', () => {
  it.each([
    [0, 'danger'],
    [14.9, 'danger'],
    [15, 'warning'],
    [29.9, 'warning'],
    [30, 'success'],
    [100, 'success'],
  ])('reads %s %% as %s', (pct, variant) => {
    expect(fuelLevelVariant(pct)).toBe(variant)
  })

  it('has no variant when the car reports no level', () => {
    expect(fuelLevelVariant(null)).toBeUndefined()
  })
})

describe('evLevelVariant', () => {
  it.each([
    [0, 'danger'],
    [19.9, 'danger'],
    [20, 'warning'],
    [49.9, 'warning'],
    [50, 'success'],
    [100, 'success'],
  ])('reads %s %% as %s', (pct, variant) => {
    expect(evLevelVariant(pct)).toBe(variant)
  })

  it('has no variant when the car reports no charge', () => {
    expect(evLevelVariant(null)).toBeUndefined()
  })
})
