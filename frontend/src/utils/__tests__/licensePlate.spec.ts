import { describe, it, expect } from 'vitest'
import { caretAfter, countPlateCharacters, formatPlate, normalizePlate } from '@/utils/licensePlate'

describe('formatPlate', () => {
  // Every Dutch side code in use, one plate each.
  it.each([
    ['AB1234', 'AB-12-34'], // 1: XX-99-99
    ['1234AB', '12-34-AB'], // 2: 99-99-XX
    ['12AB34', '12-AB-34'], // 3: 99-XX-99
    ['AB12CD', 'AB-12-CD'], // 4: XX-99-XX
    ['ABCD12', 'AB-CD-12'], // 5: XX-XX-99
    ['12ABCD', '12-AB-CD'], // 6: 99-XX-XX
    ['12ABC3', '12-ABC-3'], // 7: 99-XXX-9
    ['1ABC23', '1-ABC-23'], // 8: 9-XXX-99
    ['AB123C', 'AB-123-C'], // 9: XX-999-X
    ['A123BC', 'A-123-BC'], // 10: X-999-XX
    ['ABC12D', 'ABC-12-D'], // 11: XXX-99-X
    ['A12BCD', 'A-12-BCD'], // 12: X-99-XXX
    ['1AB234', '1-AB-234'], // 13: 9-XX-999
    ['123AB4', '123-AB-4'], // 14: 999-XX-9
  ])('writes %s as %s', (plate, written) => {
    expect(formatPlate(plate)).toBe(written)
  })

  it('takes a plate typed any way', () => {
    expect(formatPlate('hpk 98 b')).toBe('HPK-98-B')
    expect(formatPlate('hp-k98-b')).toBe('HPK-98-B')
  })

  it('places the dashes while the plate is still being typed', () => {
    expect(formatPlate('A')).toBe('A')
    expect(formatPlate('AB')).toBe('AB')
    expect(formatPlate('AB1')).toBe('AB-1')
    expect(formatPlate('AB123')).toBe('AB-123')
    expect(formatPlate('AB1234')).toBe('AB-12-34')
  })

  it('is empty for nothing', () => {
    expect(formatPlate('')).toBe('')
    expect(formatPlate(' - ')).toBe('')
  })
})

describe('normalizePlate', () => {
  it('keeps letters and digits, upper case, eight at most', () => {
    expect(normalizePlate('ab-12.cd')).toBe('AB12CD')
    expect(normalizePlate('ABCD12345')).toBe('ABCD1234')
  })
})

describe('caretAfter', () => {
  it('counts only the letters and digits', () => {
    expect(caretAfter('AB-12-34', 0)).toBe(0)
    expect(caretAfter('AB-12-34', 2)).toBe(2)
    expect(caretAfter('AB-12-34', 3)).toBe(4)
    expect(caretAfter('AB-12-34', 6)).toBe(8)
    expect(caretAfter('AB', 5)).toBe(2)
  })

  it('pairs with countPlateCharacters', () => {
    expect(countPlateCharacters('ab-1')).toBe(3)
  })
})
