import { describe, it, expect } from 'vitest'
import { shallowRef } from 'vue'
import { useMeasureField } from '../useMeasureField'
import { METRIC_UNITS, UnitFormatter } from '@/utils/units'

const t = (key: string) => key
const miles = shallowRef(new UnitFormatter({ ...METRIC_UNITS, distance: 'mi' }, t))
const kilometres = shallowRef(new UnitFormatter(METRIC_UNITS, t))
const fahrenheit = shallowRef(new UnitFormatter({ ...METRIC_UNITS, temperature: 'fahrenheit' }, t))

describe('useMeasureField', () => {
  it('shows a stored distance in whole units of the browser', () => {
    const field = useMeasureField(miles, 'distance')
    field.load(15_000)

    expect(field.shown).toBe(9321)
  })

  it('sends back the exact kilometres it loaded when left untouched', () => {
    const field = useMeasureField(miles, 'distance')
    field.load(15_000)

    expect(field.metric()).toBe(15_000)
  })

  it('converts what was typed back to kilometres', () => {
    const field = useMeasureField(miles, 'distance')
    field.load(15_000)
    field.shown = 10_000

    expect(field.metric()).toBeCloseTo(16_093.44)
  })

  it('passes kilometres straight through', () => {
    const field = useMeasureField(kilometres, 'distance')
    field.load(null)
    field.shown = 12_500

    expect(field.metric()).toBe(12_500)
  })

  it('treats an emptied field as no value', () => {
    const field = useMeasureField(miles, 'distance')
    field.load(15_000)
    field.shown = ''

    expect(field.metric()).toBeNull()
  })

  it('shows a stored temperature in whole degrees Fahrenheit and converts a typed one back', () => {
    const field = useMeasureField(fahrenheit, 'temperature')
    field.load(4.4)

    expect(field.shown).toBe(40)
    expect(field.metric()).toBe(4.4)

    field.shown = 50
    expect(field.metric()).toBeCloseTo(10)
  })
})
