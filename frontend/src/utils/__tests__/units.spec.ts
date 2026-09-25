import { describe, it, expect } from 'vitest'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import nl from '@/locales/nl.json'
import {
  DISTANCE_UNITS,
  FUEL_CONSUMPTION_UNITS,
  METRIC_UNITS,
  ODOMETER_FORMAT,
  PRESSURE_UNITS,
  TEMPERATURE_UNITS,
  UnitFormatter,
  type Quantity,
  type UnitPreferences,
} from '../units'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en, nl } })
const t = (key: string) => i18n.global.t(key)

function formatter(preferences: Partial<UnitPreferences> = {}) {
  return new UnitFormatter({ ...METRIC_UNITS, ...preferences }, t)
}

const QUANTITIES: Quantity[] = [
  'distance',
  'speed',
  'energyPerDistance',
  'distancePerPercent',
  'pressure',
  'temperature',
  'fuelConsumption',
  'volume',
]

describe('UnitFormatter', () => {
  it('shows metric values as they come, in the unit the API uses', () => {
    const units = formatter()

    expect(units.format('distance', 12.34)).toBe('12.3 km')
    expect(units.format('speed', 87.6)).toBe('88 km/h')
    expect(units.format('energyPerDistance', 164.4)).toBe('164 Wh/km')
    expect(units.format('pressure', 2.5)).toBe('2.50 bar')
    expect(units.format('temperature', 21.46)).toBe('21.5 °C')
    expect(units.format('fuelConsumption', 5.26)).toBe('5.3 L/100 km')
    expect(units.format('volume', 2.44)).toBe('2.4 L')
  })

  it('converts distance, speed and everything per distance to miles', () => {
    const units = formatter({ distance: 'mi' })

    expect(units.format('distance', 100)).toBe('62.1 mi')
    expect(units.format('speed', 130)).toBe('81 mph')
    // A mile takes 1.6 times the energy a kilometre does.
    expect(units.format('energyPerDistance', 160)).toBe('257 Wh/mi')
    expect(units.format('distancePerPercent', 8)).toBe('5.0 mi/%')
  })

  it('reads tyre pressure in whole psi and whole kPa', () => {
    expect(formatter({ pressure: 'psi' }).format('pressure', 2.55)).toBe('37 psi')
    expect(formatter({ pressure: 'kpa' }).format('pressure', 2.55)).toBe('255 kPa')
  })

  it('converts temperatures to Fahrenheit and back', () => {
    const units = formatter({ temperature: 'fahrenheit' })

    expect(units.format('temperature', 21.5)).toBe('70.7 °F')
    expect(units.format('temperature', -40)).toBe('-40.0 °F')
    expect(units.toMetric('temperature', 212)).toBeCloseTo(100)
  })

  it('turns litres per 100 km into the miles per gallon of either gallon', () => {
    expect(formatter({ fuelConsumption: 'mpgUk' }).format('fuelConsumption', 5)).toBe('56.5 mpg')
    expect(formatter({ fuelConsumption: 'mpgUs' }).format('fuelConsumption', 5)).toBe('47.0 mpg')
    expect(formatter({ fuelConsumption: 'mpgUs' }).toMetric('fuelConsumption', 47.04)).toBeCloseTo(
      5,
      2,
    )
  })

  it('counts fuel in the gallon the consumption is quoted in', () => {
    expect(formatter({ fuelConsumption: 'mpgUk' }).format('volume', 4.54609)).toBe('1.00 gal')
    expect(formatter({ fuelConsumption: 'mpgUs' }).format('volume', 3.785411784)).toBe('1.00 gal')
  })

  it('knows mpg counts the other way round from L/100 km', () => {
    expect(formatter().isReciprocal('fuelConsumption')).toBe(false)
    expect(formatter({ fuelConsumption: 'mpgUk' }).isReciprocal('fuelConsumption')).toBe(true)
  })

  it('takes a typed value back to the metric unit the API stores', () => {
    const units = formatter({ distance: 'mi', pressure: 'psi' })

    expect(units.toMetric('distance', 10_000)).toBeCloseTo(16_093.44)
    expect(units.toMetric('pressure', 36)).toBeCloseTo(2.482, 3)
  })

  it('shows nothing for a missing value, or one the unit cannot express', () => {
    expect(formatter().measure('distance', null)).toBeNull()
    expect(formatter({ fuelConsumption: 'mpgUk' }).measure('fuelConsumption', 0)).toBeNull()
  })

  it('keeps value and unit apart for cards that style them apart', () => {
    expect(formatter({ distance: 'mi' }).measure('distance', 42)).toEqual({
      value: '26.1',
      unit: 'mi',
    })
  })

  it('lets the caller choose the decimals and group the thousands', () => {
    const units = formatter()

    expect(units.format('speed', 57.26, { decimals: 1 })).toBe('57.3 km/h')
    expect(units.format('distance', 123_456.7, ODOMETER_FORMAT)).toBe(
      `${(123_457).toLocaleString()} km`,
    )
  })

  it('has a translated symbol for every unit a browser can choose, in every language', () => {
    for (const locale of ['en', 'nl'] as const) {
      i18n.global.locale.value = locale
      for (const distance of DISTANCE_UNITS)
        for (const temperature of TEMPERATURE_UNITS)
          for (const pressure of PRESSURE_UNITS)
            for (const fuelConsumption of FUEL_CONSUMPTION_UNITS) {
              const units = new UnitFormatter(
                { distance, temperature, pressure, fuelConsumption },
                t,
              )
              for (const quantity of QUANTITIES)
                expect(units.symbol(quantity)).not.toMatch(/^units\./)
            }
    }
    i18n.global.locale.value = 'en'
  })

  it('has a settings label for every unit a browser can choose, in every language', () => {
    const units = [
      ...DISTANCE_UNITS,
      ...TEMPERATURE_UNITS,
      ...PRESSURE_UNITS,
      ...FUEL_CONSUMPTION_UNITS,
    ]
    for (const messages of [en, nl])
      expect(Object.keys(messages.settings.units.options).sort()).toEqual([...units].sort())
  })
})
