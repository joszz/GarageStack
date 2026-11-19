import { describe, expect, it } from 'vitest'
import {
  isSavedApiPath,
  pickSavedStandIn,
  SAVED_AT_HEADER,
  savedAtOf,
  supersededBy,
} from '../savedApi'

const CAR = '/api/vehicles/FAKEVN00000000001'
const ORIGIN = 'https://garage.example'
const DAY = 24 * 60 * 60 * 1000
const at = (iso: string) => Date.parse(iso)

describe('isSavedApiPath', () => {
  it.each([
    '/api/vehicles',
    '/api/vehicles/tyre-pressure-thresholds',
    `${CAR}/status`,
    `${CAR}/config`,
    `${CAR}/stats`,
    `${CAR}/stats/daily`,
    `${CAR}/tyres`,
    `${CAR}/rdw`,
    `${CAR}/charge-sessions`,
    `${CAR}/charge-sessions/power`,
    `${CAR}/trips`,
    `${CAR}/trips/latest`,
    `${CAR}/trips/log`,
    `${CAR}/trips/7/editor`,
    `${CAR}/maintenance`,
    `${CAR}/maintenance/12/log`,
    `${CAR}/climate-schedules`,
    `${CAR}/commute-places`,
    '/api/notifications',
  ])('keeps %s', (path) => {
    expect(isSavedApiPath(path)).toBe(true)
  })

  it.each([
    '/api/logs',
    '/api/export',
    '/api/diagnostics',
    '/api/auth/me',
    '/api/settings',
    '/api/tokens',
    '/api/map/poi',
    `${CAR}/trips/7/points`,
    `${CAR}/phone-trackers`,
    `${CAR}/commands/lock`,
    `${CAR}/status/extra`,
  ])('leaves %s to the server', (path) => {
    expect(isSavedApiPath(path)).toBe(false)
  })
})

describe('pickSavedStandIn', () => {
  const daily = (from: string, to: string) =>
    `${ORIGIN}${CAR}/stats/daily?from=${from}&to=${to}&timeZone=Europe%2FAmsterdam`

  it('takes "the last 30 days" saved yesterday for the same period today', () => {
    const saved = [{ url: daily('2026-10-18', '2026-11-16'), savedAt: at('2026-11-16T20:00:00Z') }]

    expect(
      pickSavedStandIn(daily('2026-10-19', '2026-11-17'), at('2026-11-17T08:00:00Z'), saved),
    ).toBe(saved[0]!.url)
  })

  it('never stands in another period: 7 days for 30', () => {
    const saved = [{ url: daily('2026-11-10', '2026-11-16'), savedAt: at('2026-11-16T20:00:00Z') }]

    expect(
      pickSavedStandIn(daily('2026-10-19', '2026-11-17'), at('2026-11-17T08:00:00Z'), saved),
    ).toBeNull()
  })

  it('takes the closest in length, then the newest', () => {
    const saved = [
      { url: daily('2026-10-10', '2026-11-08'), savedAt: at('2026-11-08T20:00:00Z') },
      { url: daily('2026-10-17', '2026-11-15'), savedAt: at('2026-11-15T20:00:00Z') },
      { url: daily('2026-10-18', '2026-11-16'), savedAt: at('2026-11-16T20:00:00Z') },
    ]

    expect(
      pickSavedStandIn(daily('2026-10-19', '2026-11-17'), at('2026-11-17T08:00:00Z'), saved),
    ).toBe(saved[2]!.url)
  })

  it('needs every other parameter to match', () => {
    const saved = [
      {
        url: `${ORIGIN}${CAR}/stats/daily?from=2026-10-18&to=2026-11-16&timeZone=UTC`,
        savedAt: at('2026-11-16T20:00:00Z'),
      },
    ]

    expect(
      pickSavedStandIn(daily('2026-10-19', '2026-11-17'), at('2026-11-17T08:00:00Z'), saved),
    ).toBeNull()
  })

  it('takes a period without an end ("the last 7 days" on the map) saved earlier', () => {
    const now = at('2026-11-18T12:00:00Z')
    const savedAt = at('2026-11-17T09:30:00Z')
    const trips = (from: number) =>
      `${ORIGIN}${CAR}/trips?from=${new Date(from).toISOString()}&format=compact`
    const saved = [{ url: trips(savedAt - 7 * DAY), savedAt }]

    expect(pickSavedStandIn(trips(now - 7 * DAY), now, saved)).toBe(saved[0]!.url)
  })

  it('takes "this year" with a day less in it: the same start', () => {
    const now = at('2026-11-18T12:00:00Z')
    const stats = (to: string) => `${ORIGIN}${CAR}/stats?from=2025-12-31T23:00:00.000Z&to=${to}`
    const saved = [{ url: stats('2026-11-17T23:00:00.000Z'), savedAt: at('2026-11-17T12:00:00Z') }]

    expect(pickSavedStandIn(stats('2026-11-18T23:00:00.000Z'), now, saved)).toBe(saved[0]!.url)
  })

  it('answers a fixed period (a month of the trip log) only with its own address', () => {
    const log = (from: string, to: string) => `${ORIGIN}${CAR}/trips/log?from=${from}&to=${to}`
    const saved = [
      {
        url: log('2026-08-31T22:00:00.000Z', '2026-09-30T22:00:00.000Z'),
        savedAt: at('2026-11-10T12:00:00Z'),
      },
    ]

    expect(
      pickSavedStandIn(
        log('2026-09-30T22:00:00.000Z', '2026-10-31T23:00:00.000Z'),
        at('2026-11-18T12:00:00Z'),
        saved,
      ),
    ).toBeNull()
  })

  it('has nothing for a request without a period', () => {
    const url = `${ORIGIN}${CAR}/status`

    expect(pickSavedStandIn(url, Date.now(), [{ url, savedAt: Date.now() }])).toBeNull()
  })
})

describe('supersededBy', () => {
  it('drops the same moving period saved on an earlier visit, and nothing else', () => {
    const savedAt = at('2026-11-18T12:00:00Z')
    const trips = (from: number) =>
      `${ORIGIN}${CAR}/trips?from=${new Date(from).toISOString()}&format=compact`
    const earlier = { url: trips(savedAt - DAY - 7 * DAY), savedAt: savedAt - DAY }
    const otherPeriod = { url: trips(savedAt - 30 * DAY), savedAt: savedAt - 60_000 }
    const newest = { url: trips(savedAt - 7 * DAY), savedAt }

    expect(supersededBy(newest.url, savedAt, [earlier, otherPeriod, newest])).toEqual([earlier.url])
  })

  it('drops nothing for an answer without a period', () => {
    expect(supersededBy(`${ORIGIN}${CAR}/status`, Date.now(), [])).toEqual([])
  })
})

describe('savedAtOf', () => {
  it('reads the stamp of a saved answer', () => {
    const response = new Response('{}', {
      headers: { [SAVED_AT_HEADER]: '2026-11-18T10:00:00.000Z' },
    })

    expect(savedAtOf(response)).toBe('2026-11-18T10:00:00.000Z')
  })

  it('is null for the server answering', () => {
    expect(savedAtOf(new Response('{}'))).toBeNull()
  })
})
