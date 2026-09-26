import { describe, it, expect } from 'vitest'
import { formatIntervalSummary } from '@/utils/maintenance'
import { METRIC_UNITS, UnitFormatter } from '@/utils/units'

const t = (key: string, named?: Record<string, unknown>) =>
  named ? `${key}:${Object.values(named).join(',')}` : key
const units = new UnitFormatter(METRIC_UNITS, t)

describe('formatIntervalSummary', () => {
  it('joins distance and time intervals with the localized "or"', () => {
    expect(formatIntervalSummary({ intervalKm: 15000, intervalMonths: 12 }, t, units)).toBe(
      'maintenance.everyDistance:15,000 units.km maintenance.or maintenance.everyMonths:12',
    )
  })

  it('renders the distance in the unit the browser shows', () => {
    const miles = new UnitFormatter({ ...METRIC_UNITS, distance: 'mi' }, t)
    expect(formatIntervalSummary({ intervalKm: 15000, intervalMonths: null }, t, miles)).toBe(
      'maintenance.everyDistance:9,321 units.mi',
    )
  })

  it('renders only the interval that is set', () => {
    expect(formatIntervalSummary({ intervalKm: null, intervalMonths: 6 }, t, units)).toBe(
      'maintenance.everyMonths:6',
    )
  })

  it('is empty when neither interval is set', () => {
    expect(formatIntervalSummary({ intervalKm: null, intervalMonths: null }, t, units)).toBe('')
  })
})
