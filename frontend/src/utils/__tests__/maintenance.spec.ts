import { describe, it, expect } from 'vitest'
import { costTotals, dueStatusVariant, formatIntervalSummary } from '@/utils/maintenance'
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

describe('dueStatusVariant', () => {
  it('colours each due status, with an item never serviced as information', () => {
    expect(
      (['overdue', 'dueSoon', 'ok', 'unknown'] as const).map((status) => dueStatusVariant(status)),
    ).toEqual(['danger', 'warning', 'success', 'info'])
  })
})

describe('costTotals', () => {
  const today = new Date(2026, 10, 23)

  it('adds the costs up by the local year each service fell in', () => {
    expect(
      costTotals(
        [
          { performedAt: '2026-03-01T00:00:00', cost: 89.95 },
          { performedAt: '2026-11-02T00:00:00', cost: 412 },
          { performedAt: '2025-06-01T00:00:00', cost: 118.75 },
          { performedAt: '2021-06-01T00:00:00', cost: 685 },
        ],
        today,
      ),
    ).toEqual({ thisYear: 501.95, lastYear: 118.75, allTime: 1305.7 })
  })

  it('has no totals without a cost', () => {
    expect(costTotals([], today)).toBeNull()
  })
})
