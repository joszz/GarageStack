import { describe, it, expect } from 'vitest'
import { buildTripRow, formatTripDuration, type TripRowContext } from '../tripRows'
import type { Trip } from '@/services/vehicleApi'
import { METRIC_UNITS, UnitFormatter } from '@/utils/units'

// Stands in for vue-i18n: returns the key's last segment with its parameters, which keeps the
// assertions about composition rather than about a locale's wording.
const t = (key: string, named?: Record<string, unknown>) => {
  if (key.startsWith('units.')) return key.slice('units.'.length)
  if (key === 'trips.points') return 'pts'
  if (key === 'trips.durationMinutes') return `${named!.n} min`
  return `${named!.h}h ${named!.m}m`
}

function trip(overrides: Partial<Trip> = {}): Trip {
  return {
    index: 0,
    id: 1,
    startedAt: '2026-09-24T14:32:00.000Z',
    endedAt: '2026-09-24T15:06:00.000Z',
    distanceKm: 42,
    pointCount: 56,
    points: [
      { recordedAt: '2026-09-24T14:32:00.000Z', latitude: 52.5123, longitude: 6.0921, speed: 0 },
      { recordedAt: '2026-09-24T15:06:00.000Z', latitude: 52.2554, longitude: 6.1639, speed: 0 },
    ],
    ...overrides,
  }
}

function context(overrides: Partial<TripRowContext> = {}): TripRowContext {
  return {
    fromCity: null,
    toCity: null,
    canResolve: true,
    resolving: false,
    locale: 'en-US',
    t,
    units: new UnitFormatter(METRIC_UNITS, t),
    ...overrides,
  }
}

describe('buildTripRow', () => {
  it('leads with the route once both cities are known', () => {
    const row = buildTripRow(trip(), context({ fromCity: 'Zwolle', toCity: 'Deventer' }))

    expect(row.mode).toBe('route')
    expect(row.from).toBe('Zwolle')
    expect(row.to).toBe('Deventer')
    expect(row.routeTitle).toBe('Zwolle → Deventer')
  })

  it('names a round trip once instead of on both sides of an arrow', () => {
    const row = buildTripRow(trip(), context({ fromCity: 'Zwolle', toCity: 'Zwolle' }))

    expect(row.mode).toBe('route')
    expect(row.from).toBe('Zwolle')
    expect(row.to).toBeNull()
  })

  it('names a trip in progress after where it started, ignoring the live position', () => {
    const row = buildTripRow(
      trip(),
      context({ fromCity: 'Amsterdam', toCity: 'Haarlem', inProgress: true }),
    )

    expect(row.mode).toBe('route')
    expect(row.from).toBe('Amsterdam')
    expect(row.to).toBeNull()
    expect(row.routeTitle).toBe('Amsterdam')
  })

  it('shows a trip in progress as soon as its origin is known', () => {
    const row = buildTripRow(
      trip(),
      context({ fromCity: 'Amsterdam', toCity: null, inProgress: true, resolving: true }),
    )

    expect(row.mode).toBe('route')
  })

  it('waits rather than showing half a route', () => {
    const row = buildTripRow(trip(), context({ fromCity: 'Zwolle', resolving: true }))

    expect(row.mode).toBe('pending')
  })

  it('falls back to the date when nothing is resolving any more', () => {
    const row = buildTripRow(trip(), context({ resolving: false }))

    expect(row.mode).toBe('date')
  })

  it('falls back to the date when geocoding cannot answer at all', () => {
    const row = buildTripRow(trip(), context({ canResolve: false, resolving: true }))

    expect(row.mode).toBe('date')
  })

  it('moves the date into the meta line when the route takes the headline', () => {
    const row = buildTripRow(trip(), context({ fromCity: 'Zwolle', toCity: 'Deventer' }))

    expect(row.meta).toBe('Sep 24 · 42.0 km · 34 min')
    expect(row.meta).not.toContain('pts')
  })

  it('keeps the meta line it has always shown when the date leads', () => {
    const row = buildTripRow(trip(), context())

    expect(row.meta).toBe('42.0 km · 34 min · 56 pts')
  })

  it('gives the distance in the unit the browser shows', () => {
    const miles = new UnitFormatter({ ...METRIC_UNITS, distance: 'mi' }, t)
    const row = buildTripRow(trip(), context({ units: miles }))

    expect(row.meta).toBe('26.1 mi · 34 min · 56 pts')
  })

  it('always offers the full detail as a tooltip', () => {
    const row = buildTripRow(trip(), context({ fromCity: 'Zwolle', toCity: 'Deventer' }))

    expect(row.metaTitle).toContain('42.0 km')
    expect(row.metaTitle).toContain('34 min')
    expect(row.metaTitle).toContain('56 pts')
    expect(row.metaTitle).toContain(row.dateLabel)
    expect(row.metaTitle).toContain(row.timeLabel)
  })

  it('formats the date and time in the given locale', () => {
    const dutch = buildTripRow(trip(), context({ locale: 'nl-NL' }))
    const english = buildTripRow(trip(), context({ locale: 'en-US' }))

    expect(dutch.dateLabel).not.toBe(english.dateLabel)
  })
})

describe('formatTripDuration', () => {
  it('reports minutes under an hour', () => {
    expect(formatTripDuration('2026-09-24T14:00:00Z', '2026-09-24T14:34:00Z', t)).toBe('34 min')
  })

  it('reports hours and minutes past an hour', () => {
    expect(formatTripDuration('2026-09-24T14:00:00Z', '2026-09-24T16:05:00Z', t)).toBe('2h 5m')
  })
})
