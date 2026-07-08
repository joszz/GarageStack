import { describe, it, expect } from 'vitest'
import { canonicalFuelBrand } from '@/utils/fuelBrands'

describe('canonicalFuelBrand', () => {
  it.each([
    ['BP', 'BP'],
    ['BP express', 'BP'],
    ['Shell', 'Shell'],
    ['Shell Express', 'Shell'],
    ['Esso', 'Esso'],
    ['Esso Express', 'Esso'],
    ['TotalEnergies', 'TotalEnergies'],
    ['TotalEnergies Express', 'TotalEnergies'],
    ['Texaco', 'Texaco'],
    ['Texaco Pouw BV', 'Texaco'],
    ['Tamoil', 'Tamoil'],
    ['Tamoil express', 'Tamoil'],
    ['Argos', 'Argos'],
    ['ArgosOil', 'Argos'],
    ['Avia', 'Avia'],
    ['AVIA', 'Avia'],
    ['Avia Marees', 'Avia'],
    ['AVIA Truck', 'Avia'],
    ['Avia Xpress', 'Avia'],
    ['AVIA XPress', 'Avia'],
    ['Lukoil', 'Lukoil'],
    ['Lukoil Express', 'Lukoil'],
    ['OK', 'OK'],
    ['OK express', 'OK'],
    ['Haan', 'Haan'],
    ['Haan Express', 'Haan'],
    ['T-Energy', 'T-Energy'],
    ['T-Energy express', 'T-Energy'],
    ['Pin&Go', 'Pin&Go'],
    ['Pin&GO', 'Pin&Go'],
    ['De meeuw', 'De Meeuw'],
    ['De Meeuw', 'De Meeuw'],
    ['Fieten olie', 'Fieten Olie'],
    ['Fieten Olie', 'Fieten Olie'],
    ['Dirk', 'Dirk'],
    ['Dirk vd Broek', 'Dirk'],
  ])('coalesces %s to %s', (raw, expected) => {
    expect(canonicalFuelBrand(raw)).toBe(expected)
  })

  it.each(['Q8', 'Gulf', 'TinQ', 'Tango', 'GT24', 'Skippy', 'Autofood'])(
    'leaves unrelated brand %s unchanged',
    (raw) => {
      expect(canonicalFuelBrand(raw)).toBe(raw)
    },
  )

  it('trims whitespace', () => {
    expect(canonicalFuelBrand('  BP express  ')).toBe('BP')
  })
})
