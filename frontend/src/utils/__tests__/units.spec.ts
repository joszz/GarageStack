import { describe, it, expect } from 'vitest'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import nl from '@/locales/nl.json'
import { i18n as appI18n } from '@/i18n'
import {
  AUTO_UNITS,
  DISTANCE_UNITS,
  ENERGY_CONSUMPTION_UNITS,
  FUEL_CONSUMPTION_UNITS,
  METRIC_UNITS,
  ODOMETER_FORMAT,
  PRESSURE_UNITS,
  regionUnits,
  resolveUnits,
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
    expect(units.format('energyPerDistance', 164.4)).toBe('16.4 kWh/100 km')
    expect(units.format('pressure', 2.5)).toBe('2.50 bar')
    expect(units.format('temperature', 21.46)).toBe('21.5 °C')
    expect(units.format('fuelConsumption', 5.26)).toBe('5.3 L/100 km')
    expect(units.format('volume', 2.44)).toBe('2.4 L')
  })

  it('converts distance and speed to miles without changing the chosen consumption unit', () => {
    const units = formatter({ distance: 'mi' })

    expect(units.format('distance', 100)).toBe('62.1 mi')
    expect(units.format('speed', 130)).toBe('81 mph')
    expect(units.format('distancePerPercent', 8)).toBe('5.0 mi/%')
  })

  it('converts electric consumption to every selectable unit', () => {
    expect(formatter({ energyConsumption: 'whPerKm' }).format('energyPerDistance', 160)).toBe(
      '160 Wh/km',
    )
    expect(formatter({ energyConsumption: 'whPerMi' }).format('energyPerDistance', 160)).toBe(
      '257 Wh/mi',
    )
    expect(
      formatter({ energyConsumption: 'kwhPer100Km' }).format('energyPerDistance', 160),
    ).toBe('16.0 kWh/100 km')
    expect(
      formatter({ energyConsumption: 'kwhPer100Mi' }).format('energyPerDistance', 160),
    ).toBe('25.7 kWh/100 mi')
    expect(formatter({ energyConsumption: 'kmPerKwh' }).format('energyPerDistance', 160)).toBe(
      '6.3 km/kWh',
    )
    expect(formatter({ energyConsumption: 'miPerKwh' }).format('energyPerDistance', 160)).toBe(
      '3.9 mi/kWh',
    )
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

  it('converts reciprocal energy efficiency back to canonical Wh/km', () => {
    const units = formatter({ energyConsumption: 'kmPerKwh' })
    expect(units.isReciprocal('energyPerDistance')).toBe(true)
    expect(units.toMetric('energyPerDistance', 6.25)).toBeCloseTo(160)
    expect(formatter({ energyConsumption: 'kwhPer100Km' }).isReciprocal('energyPerDistance')).toBe(
      false,
    )
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
    expect(units.format('distance', 123_456.7, ODOMETER_FORMAT)).toBe('123,457 km')
  })

  it('writes numbers the way the interface language does', () => {
    const units = formatter()
    appI18n.global.locale.value = 'nl'
    try {
      expect(units.format('speed', 57.26, { decimals: 1 })).toBe('57,3 km/h')
      expect(units.format('distance', 123_456.7, ODOMETER_FORMAT)).toBe('123.457 km')
    } finally {
      appI18n.global.locale.value = 'en'
    }
  })

  it('has a translated symbol for every unit a browser can choose, in every language', () => {
    for (const locale of ['en', 'nl'] as const) {
      i18n.global.locale.value = locale
      for (const distance of DISTANCE_UNITS)
        for (const temperature of TEMPERATURE_UNITS)
          for (const pressure of PRESSURE_UNITS)
            for (const fuelConsumption of FUEL_CONSUMPTION_UNITS)
              for (const energyConsumption of ENERGY_CONSUMPTION_UNITS) {
                const units = new UnitFormatter(
                  { distance, temperature, pressure, fuelConsumption, energyConsumption },
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
      ...ENERGY_CONSUMPTION_UNITS,
    ]
    for (const messages of [en, nl])
      expect(Object.keys(messages.settings.units.options).sort()).toEqual([...units].sort())
  })
})

describe('regionUnits', () => {
  it('is metric with bar on most of the map', () => {
    expect(regionUnits('NL')).toEqual(METRIC_UNITS)
    expect(regionUnits('DE')).toEqual(METRIC_UNITS)
  })

  it('counts in miles, Fahrenheit, psi and US gallons in the United States', () => {
    expect(regionUnits('US')).toEqual({
      distance: 'mi',
      temperature: 'fahrenheit',
      pressure: 'psi',
      fuelConsumption: 'mpgUs',
      energyConsumption: 'miPerKwh',
    })
  })

  it('counts in miles, psi and UK gallons but Celsius in the United Kingdom', () => {
    expect(regionUnits('GB')).toEqual({
      distance: 'mi',
      temperature: 'celsius',
      pressure: 'psi',
      fuelConsumption: 'mpgUk',
      energyConsumption: 'kwhPer100Mi',
    })
  })

  it('pumps tyres in psi in Australia and in kPa in Japan, with everything else metric', () => {
    expect(regionUnits('AU')).toEqual({ ...METRIC_UNITS, pressure: 'psi' })
    expect(regionUnits('JP')).toEqual({ ...METRIC_UNITS, pressure: 'kpa' })
  })
})

describe('resolveUnits', () => {
  it('takes each unit left on automatic from the region and keeps every chosen one', () => {
    expect(resolveUnits(AUTO_UNITS, 'US')).toEqual(regionUnits('US'))
    expect(resolveUnits({ ...AUTO_UNITS, distance: 'km', pressure: 'bar' }, 'US')).toEqual({
      distance: 'km',
      temperature: 'fahrenheit',
      pressure: 'bar',
      fuelConsumption: 'mpgUs',
      energyConsumption: 'miPerKwh',
    })
  })
})
