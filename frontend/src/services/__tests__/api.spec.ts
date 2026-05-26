import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'

import { vehicleApi } from '@/services/api'

function makeResponse(status: number, body?: unknown) {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: () => Promise.resolve(body),
  }
}

describe('vehicleApi', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('list() resolves with the parsed JSON body', async () => {
    const vehicles = [{ id: 1, vin: 'ABC123' }]
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(makeResponse(200, vehicles)))
    expect(await vehicleApi.list()).toEqual(vehicles)
  })

  it('status() returns undefined on 204 No Content', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(makeResponse(204)))
    expect(await vehicleApi.status('VIN1')).toBeUndefined()
  })

  it('throws on a non-200 error status', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(makeResponse(500)))
    await expect(vehicleApi.list()).rejects.toThrow('API error 500')
  })

  it('history() appends from and to as query params', async () => {
    const fetchSpy = vi.fn().mockResolvedValue(makeResponse(200, []))
    vi.stubGlobal('fetch', fetchSpy)
    await vehicleApi.history('VIN1', '2024-01-01', '2024-01-31')
    const firstCall = fetchSpy.mock.calls[0]
    expect(firstCall).toBeDefined()
    if (!firstCall) throw new Error('Expected first fetch call to exist')
    const [url] = firstCall
    expect(url).toContain('from=2024-01-01')
    expect(url).toContain('to=2024-01-31')
  })

  it('history() omits query params when not provided', async () => {
    const fetchSpy = vi.fn().mockResolvedValue(makeResponse(200, []))
    vi.stubGlobal('fetch', fetchSpy)
    await vehicleApi.history('VIN1')
    const firstCall = fetchSpy.mock.calls[0]
    expect(firstCall).toBeDefined()
    if (!firstCall) throw new Error('Expected first fetch call to exist')
    const [url] = firstCall
    expect(url).not.toContain('from=')
    expect(url).not.toContain('to=')
  })

  it('sendCommand() posts the value as a JSON body', async () => {
    const fetchSpy = vi.fn().mockResolvedValue(makeResponse(200))
    vi.stubGlobal('fetch', fetchSpy)
    await vehicleApi.sendCommand('VIN1', 'lock', 'lock')
    const firstCall = fetchSpy.mock.calls[0]
    expect(firstCall).toBeDefined()
    if (!firstCall) throw new Error('Expected first fetch call to exist')
    const [url, options] = firstCall
    expect(url).toContain('/api/vehicles/VIN1/commands/lock')
    expect(options.method).toBe('POST')
    expect(JSON.parse(options.body)).toEqual({ value: 'lock' })
  })
})
