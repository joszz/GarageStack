import { describe, it, expect } from 'vitest'
import { shallowRef } from 'vue'
import { useDistanceField } from '../useDistanceField'
import { METRIC_UNITS, UnitFormatter } from '@/utils/units'

const t = (key: string) => key
const miles = shallowRef(new UnitFormatter({ ...METRIC_UNITS, distance: 'mi' }, t))
const kilometres = shallowRef(new UnitFormatter(METRIC_UNITS, t))

describe('useDistanceField', () => {
  it('shows a stored distance in whole units of the browser', () => {
    const field = useDistanceField(miles)
    field.load(15_000)

    expect(field.shown).toBe(9321)
  })

  it('sends back the exact kilometres it loaded when left untouched', () => {
    const field = useDistanceField(miles)
    field.load(15_000)

    expect(field.km()).toBe(15_000)
  })

  it('converts what was typed back to kilometres', () => {
    const field = useDistanceField(miles)
    field.load(15_000)
    field.shown = 10_000

    expect(field.km()).toBeCloseTo(16_093.44)
  })

  it('passes kilometres straight through', () => {
    const field = useDistanceField(kilometres)
    field.load(null)
    field.shown = 12_500

    expect(field.km()).toBe(12_500)
  })

  it('treats an emptied field as no distance', () => {
    const field = useDistanceField(miles)
    field.load(15_000)
    field.shown = ''

    expect(field.km()).toBeNull()
  })
})
