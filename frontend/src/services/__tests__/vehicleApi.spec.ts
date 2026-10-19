import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'

import { vehicleApi, type CompactTrip } from '@/services/vehicleApi'
import { makeResponse, type FetchSpy } from './fetchStub'

describe('vehicleApi', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('list() resolves with the parsed JSON body', async () => {
    const vehicles = [{ id: 1, vin: 'FAKEVN00000000001' }]
    vi.stubGlobal('fetch', vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, vehicles)))
    expect(await vehicleApi.list()).toEqual(vehicles)
  })

  it('status() returns undefined on 204 No Content', async () => {
    vi.stubGlobal('fetch', vi.fn<FetchSpy>().mockResolvedValue(makeResponse(204)))
    expect(await vehicleApi.status('VIN1')).toBeUndefined()
  })

  it("dailyStats() asks for the days in the browser's time zone", async () => {
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, []))
    vi.stubGlobal('fetch', fetchSpy)
    await vehicleApi.dailyStats('VIN1', '2025-01-01', '2025-12-31', 'Europe/Amsterdam')
    const [url] = fetchSpy.mock.calls[0]!
    expect(url).toContain('/api/vehicles/VIN1/stats/daily?')
    expect(url).toContain('from=2025-01-01')
    expect(url).toContain('to=2025-12-31')
    expect(url).toContain('timeZone=Europe%2FAmsterdam')
  })

  it('tripSummaries() asks for the trips without their fixes', async () => {
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, []))
    vi.stubGlobal('fetch', fetchSpy)
    await vehicleApi.tripSummaries('VIN1', '2024-01-01')
    const [url] = fetchSpy.mock.calls[0] ?? []
    expect(url).toContain('/api/vehicles/VIN1/trips?')
    expect(url).toContain('from=2024-01-01')
    expect(url).toContain('points=false')
  })

  it('trips() asks for the compact format and unpacks each trip into its fixes', async () => {
    const compact: CompactTrip = {
      index: 0,
      id: 7,
      startedAt: '2026-10-19T07:00:00Z',
      endedAt: '2026-10-19T07:01:25Z',
      distanceKm: 0.4,
      pointCount: 2,
      endLatitude: 52.092346,
      endLongitude: 5.119877,
      maxSpeedKmh: 48.5,
      avgMovingSpeedKmh: 48.5,
      movingSpeedSamples: 1,
      // 52.091235,5.121235 then 52.092346,5.119877 at six decimals, encoded outside this code.
      line: 'eukjbBelqwHmdAzsA',
      intervalsMs: [2000, 40500],
      speeds: [null, 48.5],
    }
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, [compact]))
    vi.stubGlobal('fetch', fetchSpy)

    const [trip] = await vehicleApi.trips('VIN1', '2026-10-01')

    const [url] = fetchSpy.mock.calls[0]!
    expect(url).toContain('format=compact')
    expect(trip!.points).toEqual([
      {
        recordedAt: '2026-10-19T07:00:02.000Z',
        latitude: 52.091235,
        longitude: 5.121235,
        speed: null,
      },
      {
        recordedAt: '2026-10-19T07:00:42.500Z',
        latitude: 52.092346,
        longitude: 5.119877,
        speed: 48.5,
      },
    ])
    expect(trip).not.toHaveProperty('line')
    expect(trip!.maxSpeedKmh).toBe(48.5)
  })

  it('latestTrip() returns undefined when the vehicle has no trip', async () => {
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(204))
    vi.stubGlobal('fetch', fetchSpy)
    expect(await vehicleApi.latestTrip('VIN1')).toBeUndefined()
    const [url] = fetchSpy.mock.calls[0] ?? []
    expect(url).toContain('/api/vehicles/VIN1/trips/latest')
  })

  it('sendCommand() posts the value as a JSON body', async () => {
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200))
    vi.stubGlobal('fetch', fetchSpy)
    await vehicleApi.sendCommand('VIN1', 'lock', 'True')
    const [url, options] = fetchSpy.mock.calls[0]!
    expect(url).toContain('/api/vehicles/VIN1/commands/lock')
    expect(options!.method).toBe('POST')
    expect(JSON.parse(options!.body as string)).toEqual({ value: 'True' })
  })
})
