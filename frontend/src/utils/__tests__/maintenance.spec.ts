import { describe, it, expect } from 'vitest'
import { formatIntervalSummary } from '@/utils/maintenance'

const t = (key: string, named?: Record<string, unknown>) =>
  named ? `${key}:${Object.values(named).join(',')}` : key

describe('formatIntervalSummary', () => {
  it('joins distance and time intervals with the localized "or"', () => {
    expect(formatIntervalSummary({ intervalKm: 15000, intervalMonths: 12 }, t)).toBe(
      `maintenance.everyKm:${(15000).toLocaleString()} maintenance.or maintenance.everyMonths:12`,
    )
  })

  it('renders only the interval that is set', () => {
    expect(formatIntervalSummary({ intervalKm: null, intervalMonths: 6 }, t)).toBe(
      'maintenance.everyMonths:6',
    )
  })

  it('is empty when neither interval is set', () => {
    expect(formatIntervalSummary({ intervalKm: null, intervalMonths: null }, t)).toBe('')
  })
})
