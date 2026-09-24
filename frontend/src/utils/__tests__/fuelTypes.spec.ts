import { describe, it, expect } from 'vitest'
import { FUEL_TYPES, isFuelType, matchesFuelTypeFilter, stationFuelTypes } from '@/utils/fuelTypes'

describe('stationFuelTypes', () => {
  it.each([
    ['fuel:octane_95', 'petrol'],
    ['fuel:octane_98', 'petrol'],
    ['fuel:e10', 'petrol'],
    ['fuel:e5', 'petrol'],
    ['fuel:diesel', 'diesel'],
    ['fuel:biodiesel', 'diesel'],
    ['fuel:HGV_diesel', 'diesel'],
    ['fuel:GTL_diesel', 'diesel'],
    ['fuel:diesel:class2', 'diesel'],
    ['fuel:lpg', 'lpg'],
    ['fuel:cng', 'cng'],
    ['fuel:lng', 'lng'],
    ['fuel:LH2', 'hydrogen'],
    ['fuel:H2', 'hydrogen'],
    ['fuel:e85', 'e85'],
    ['fuel:adblue', 'adblue'],
  ])('maps %s onto %s', (tag, expected) => {
    expect(stationFuelTypes({ [tag]: 'yes' })).toEqual([expected])
  })

  it('coalesces the petrol grades into one type', () => {
    const tags = { 'fuel:octane_95': 'yes', 'fuel:octane_98': 'yes', 'fuel:e10': 'yes' }
    expect(stationFuelTypes(tags)).toEqual(['petrol'])
  })

  it('returns the types in dropdown order, not tag order', () => {
    const tags = { 'fuel:adblue': 'yes', 'fuel:diesel': 'yes', 'fuel:octane_95': 'yes' }
    expect(stationFuelTypes(tags)).toEqual(['petrol', 'diesel', 'adblue'])
  })

  it.each(['no', 'No', 'false', '0'])('treats a %s value as not sold', (value) => {
    expect(stationFuelTypes({ 'fuel:lpg': value, 'fuel:diesel': 'yes' })).toEqual(['diesel'])
  })

  it('ignores tags that are not fuel tags', () => {
    expect(stationFuelTypes({ brand: 'Shell', amenity: 'fuel', 'fuel:diesel': 'yes' })).toEqual([
      'diesel',
    ])
  })

  it('leaves electricity out, since charging has its own layer', () => {
    expect(stationFuelTypes({ 'fuel:electricity': 'yes' })).toEqual([])
  })

  it.each([null, undefined, {}])('returns nothing for %s tags', (tags) => {
    expect(stationFuelTypes(tags)).toEqual([])
  })
})

describe('matchesFuelTypeFilter', () => {
  const petrolStation = { 'fuel:octane_95': 'yes', 'fuel:e10': 'yes' }
  const dieselStation = { 'fuel:diesel': 'yes' }

  it('keeps every station when nothing is selected', () => {
    expect(matchesFuelTypeFilter(petrolStation, [])).toBe(true)
    expect(matchesFuelTypeFilter(null, [])).toBe(true)
  })

  it('keeps a station that sells a selected type', () => {
    expect(matchesFuelTypeFilter(petrolStation, ['petrol'])).toBe(true)
  })

  it('drops a station that describes its range without a selected type', () => {
    expect(matchesFuelTypeFilter(dieselStation, ['petrol'])).toBe(false)
  })

  it('keeps a station that sells any one of several selected types', () => {
    expect(matchesFuelTypeFilter(dieselStation, ['petrol', 'diesel'])).toBe(true)
  })

  const undescribed: Array<Record<string, string> | null | undefined> = [
    null,
    undefined,
    {},
    { brand: 'Shell' },
  ]

  it.each(undescribed)('keeps a station that lists no fuels at all (%s)', (tags) => {
    expect(matchesFuelTypeFilter(tags, ['petrol'])).toBe(true)
  })

  it('drops a station whose only selected type is tagged as not sold', () => {
    expect(matchesFuelTypeFilter({ 'fuel:lpg': 'no' }, ['lpg'])).toBe(false)
  })

  it('drops an electricity-only site, which describes its range but sells no fuel', () => {
    expect(matchesFuelTypeFilter({ 'fuel:electricity': 'yes' }, ['petrol'])).toBe(false)
  })

  it('ignores a selected id that is not a known type', () => {
    expect(matchesFuelTypeFilter(petrolStation, ['kerosene'])).toBe(false)
  })
})

describe('isFuelType', () => {
  it.each([...FUEL_TYPES])('accepts %s', (type) => {
    expect(isFuelType(type)).toBe(true)
  })

  it.each(['', 'kerosene', 'PETROL', 'electricity'])('rejects %s', (value) => {
    expect(isFuelType(value)).toBe(false)
  })
})
