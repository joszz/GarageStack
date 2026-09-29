import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'

import { vehicleApi } from '@/services/vehicleApi'
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

  it('history() appends from and to as query params', async () => {
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, []))
    vi.stubGlobal('fetch', fetchSpy)
    await vehicleApi.history('VIN1', '2024-01-01', '2024-01-31')
    const [url] = fetchSpy.mock.calls[0]!
    expect(url).toContain('from=2024-01-01')
    expect(url).toContain('to=2024-01-31')
  })

  it('history() omits query params when not provided', async () => {
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, []))
    vi.stubGlobal('fetch', fetchSpy)
    await vehicleApi.history('VIN1')
    const [url] = fetchSpy.mock.calls[0]!
    expect(url).not.toContain('from=')
    expect(url).not.toContain('to=')
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
