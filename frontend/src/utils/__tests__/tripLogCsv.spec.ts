import { describe, it, expect } from 'vitest'
import { deventer, logEntry, zwolle } from '@/services/__tests__/tripLogFixtures'
import { tripLogCsv, tripLogFileName } from '../tripLogCsv'
import { METRIC_UNITS, UnitFormatter } from '../units'

// Returns the key itself, with a unit parameter in brackets, so the assertions read which label
// went where.
const t = (key: string, named?: Record<string, unknown>) =>
  named ? `${key}(${String(named.unit)})` : key
const units = new UnitFormatter(METRIC_UNITS, t)
const miles = new UnitFormatter({ ...METRIC_UNITS, distance: 'mi' }, t)

function lines(csv: string): string[] {
  return csv.split('\r\n').filter((line) => line !== '')
}

describe('tripLogCsv', () => {
  const entry = logEntry({
    startedAt: new Date(2026, 8, 24, 14, 32).toISOString(),
    endedAt: new Date(2026, 8, 24, 15, 6).toISOString(),
    startPlace: zwolle,
    endPlace: deventer,
    purpose: 'business',
    notes: 'Client visit',
  })

  it('writes a header, then one line per trip in the columns a trip log asks for', () => {
    const [header, row] = lines(
      tripLogCsv([entry], { locale: 'en-US', t, units, placesShown: true }),
    )

    expect(header).toBe(
      [
        'tripLog.csv.date',
        'tripLog.csv.departed',
        'tripLog.csv.arrived',
        'tripLog.csv.from',
        'tripLog.csv.to',
        'tripLog.csv.odometerStart(units.km)',
        'tripLog.csv.odometerEnd(units.km)',
        'tripLog.csv.distance(units.km)',
        'tripLog.csv.purpose',
        'tripLog.csv.notes',
      ].join(','),
    )
    expect(row).toBe(
      '9/24/2026,02:32 PM,03:06 PM,"Grote Markt 1, 8011 PK Zwolle","Brink 2, 7411 BT Deventer",24010,24052.5,42.5,tripLog.purpose.business,Client visit',
    )
  })

  it('writes the distances in miles, and says so in the headers, for a log read in miles', () => {
    const [header, row] = lines(
      tripLogCsv([entry], { locale: 'en-US', t, units: miles, placesShown: true }),
    )

    expect(header!.split(',').slice(5, 8)).toEqual([
      'tripLog.csv.odometerStart(units.mi)',
      'tripLog.csv.odometerEnd(units.mi)',
      'tripLog.csv.distance(units.mi)',
    ])
    expect(row!.split(',').slice(7, 10)).toEqual(['14919.1', '14945.5', '26.4'])
  })

  it('follows Dutch spreadsheet conventions in Dutch', () => {
    const [, row] = lines(tripLogCsv([entry], { locale: 'nl-NL', t, units, placesShown: true }))

    expect(row).toBe(
      '24-9-2026;14:32;15:06;Grote Markt 1, 8011 PK Zwolle;Brink 2, 7411 BT Deventer;24010;24052,5;42,5;tripLog.purpose.business;Client visit',
    )
  })

  it('writes coordinates when place names are switched off, and a blank for no purpose', () => {
    const [, row] = lines(
      tripLogCsv([{ ...entry, purpose: null, notes: null }], {
        locale: 'nl-NL',
        t,
        units,
        placesShown: false,
      }),
    )

    expect(row).toBe(
      '24-9-2026;14:32;15:06;52.51230, 6.09210;52.25540, 6.16390;24010;24052,5;42,5;;',
    )
  })

  it('rounds the distance to a decimal', () => {
    const [, row] = lines(
      tripLogCsv([logEntry({ odometerStartKm: null, distanceKm: 12.3456 })], {
        locale: 'en-US',
        t,
        units,
        placesShown: false,
      }),
    )

    expect(row!.split(',').slice(-5)).toEqual(['', '24052.5', '12.3', '', ''])
  })
})

describe('tripLogFileName', () => {
  it('names the file after the period', () => {
    expect(tripLogFileName({ year: 2026, month: 8 })).toBe('trip-log-2026-09.csv')
    expect(tripLogFileName({ year: 2026, month: null })).toBe('trip-log-2026.csv')
  })
})
