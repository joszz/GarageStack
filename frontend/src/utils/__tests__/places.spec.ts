import { describe, it, expect } from 'vitest'
import { addressLabel, cityName, coordinateLabel, postalAddressLabel, streetName } from '../places'
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

describe('postalAddressLabel', () => {
  it('puts the postcode before the city, after the street', () => {
    expect(
      postalAddressLabel(
        place({ road: 'Grote Markt', houseNumber: '1', postcode: '8011 PK', city: 'Zwolle' }),
      ),
    ).toBe('Grote Markt 1, 8011 PK Zwolle')
  })

  it('thins out to whatever is known', () => {
    expect(postalAddressLabel(place({ road: 'Brink', city: 'Deventer' }))).toBe('Brink, Deventer')
    expect(postalAddressLabel(place({ postcode: '7411 BT' }))).toBe('7411 BT')
  })

  it("names a place with no street after its label's settlement, as the other labels do", () => {
    expect(postalAddressLabel(place({ displayName: 'Wijthmen, Zwolle, Nederland' }))).toBe(
      'Wijthmen',
    )
  })

  it('is null for an empty place', () => {
    expect(postalAddressLabel(place())).toBeNull()
    expect(postalAddressLabel(null)).toBeNull()
  })
})

describe('coordinateLabel', () => {
  it('writes both coordinates to five decimals', () => {
    expect(coordinateLabel(52.5, 6.0921234)).toBe('52.50000, 6.09212')
  })
})
