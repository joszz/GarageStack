import { describe, it, expect, afterEach } from 'vitest'
import { i18n } from '@/i18n'
import { currencySymbol, formatCurrency, isCurrency, regionCurrency } from '../currency'

describe('regionCurrency', () => {
  it('pays in the currency of the region, and in euros in the euro area', () => {
    expect(regionCurrency('GB')).toBe('GBP')
    expect(regionCurrency('AU')).toBe('AUD')
    expect(regionCurrency('BR')).toBe('BRL')
    expect(regionCurrency('NL')).toBe('EUR')
    expect(regionCurrency('PT')).toBe('EUR')
  })
})

describe('isCurrency', () => {
  it('knows only the currencies it offers', () => {
    expect(isCurrency('EUR')).toBe(true)
    expect(isCurrency('XYZ')).toBe(false)
    expect(isCurrency(12)).toBe(false)
  })
})

describe('formatCurrency', () => {
  afterEach(() => {
    i18n.global.locale.value = 'en'
  })

  it('writes an amount in the interface language', () => {
    expect(formatCurrency(1234.5, 'EUR')).toBe('€1,234.50')
    i18n.global.locale.value = 'nl'
    expect(formatCurrency(1234.5, 'EUR').replace(/\s/g, ' ')).toBe('€ 1.234,50')
  })

  it("names the currency's sign for a field label", () => {
    expect(currencySymbol('GBP')).toBe('£')
    expect(currencySymbol('USD')).toBe('$')
  })
})
