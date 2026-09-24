import { describe, it, expect } from 'vitest'
import { addressLabel, cityName, streetName } from '../places'
import type { Place } from '@/services/mapApi'

function place(overrides: Partial<Place> = {}): Place {
  return {
    road: null,
    houseNumber: null,
    city: null,
    postcode: null,
    countryCode: null,
    displayName: null,
    ...overrides,
  }
}

describe('cityName', () => {
  it('returns the settlement name', () => {
    expect(cityName(place({ city: 'Zwolle' }))).toBe('Zwolle')
  })

  it('falls back to the first label segment when no street is known', () => {
    expect(cityName(place({ displayName: 'Wijthmen, Zwolle, Nederland' }))).toBe('Wijthmen')
  })

  it('does not mistake a street for a city on an address-level answer', () => {
    expect(
      cityName(place({ road: 'Grote Markt', displayName: 'Grote Markt 1, Zwolle' })),
    ).toBeNull()
  })

  it('returns null for a nameless or missing place', () => {
    expect(cityName(place())).toBeNull()
    expect(cityName(null)).toBeNull()
    expect(cityName(place({ city: '   ' }))).toBeNull()
  })
})

describe('streetName', () => {
  it('combines road and house number', () => {
    expect(streetName(place({ road: 'Grote Markt', houseNumber: '1' }))).toBe('Grote Markt 1')
  })

  it('keeps just the road when there is no house number', () => {
    expect(streetName(place({ road: 'A28' }))).toBe('A28')
  })

  it('returns null without a road', () => {
    expect(streetName(place({ houseNumber: '1', city: 'Zwolle' }))).toBeNull()
  })
})

describe('addressLabel', () => {
  it('reads as street then city', () => {
    expect(addressLabel(place({ road: 'Grote Markt', houseNumber: '1', city: 'Zwolle' }))).toBe(
      'Grote Markt 1, Zwolle',
    )
  })

  it('drops the part it does not know', () => {
    expect(addressLabel(place({ city: 'Zwolle' }))).toBe('Zwolle')
    expect(addressLabel(place({ road: 'Grote Markt' }))).toBe('Grote Markt')
  })

  it('falls back to the geocoder label when it has no parts at all', () => {
    expect(addressLabel(place({ displayName: 'Noordzee' }))).toBe('Noordzee')
  })

  it('returns null for an empty answer', () => {
    expect(addressLabel(place())).toBeNull()
    expect(addressLabel(null)).toBeNull()
  })
})
