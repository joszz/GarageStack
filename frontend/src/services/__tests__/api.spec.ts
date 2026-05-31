import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'

import { vehicleApi, setUnauthorizedHandler, clearUnauthorizedState } from '@/services/api'

function makeResponse(status: number, body?: unknown) {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: () => Promise.resolve(body),
  }
}

type FetchSpy = (...args: Parameters<typeof fetch>) => Promise<ReturnType<typeof makeResponse>>

describe('unauthorizedHandler', () => {
  beforeEach(() => {
    clearUnauthorizedState()
    vi.resetAllMocks()
  })

  afterEach(() => {
    setUnauthorizedHandler(null)
    clearUnauthorizedState()
    vi.unstubAllGlobals()
  })

  it('calls the handler once on a 401 response', async () => {
    const handler = vi.fn<() => void>()
    setUnauthorizedHandler(handler)
    vi.stubGlobal('fetch', vi.fn<FetchSpy>().mockResolvedValue({ ok: false, status: 401, json: () => Promise.resolve() }))
    await vehicleApi.list().catch(() => {})
    expect(handler).toHaveBeenCalledOnce()
  })

  it('does not call the handler a second time when already handling a 401', async () => {
    const handler = vi.fn<() => void>()
    setUnauthorizedHandler(handler)
    vi.stubGlobal('fetch', vi.fn<FetchSpy>().mockResolvedValue({ ok: false, status: 401, json: () => Promise.resolve() }))
    await Promise.all([vehicleApi.list().catch(() => {}), vehicleApi.list().catch(() => {})])
    expect(handler).toHaveBeenCalledOnce()
  })

  it('calls the handler again after clearUnauthorizedState', async () => {
    const handler = vi.fn<() => void>()
    setUnauthorizedHandler(handler)
    vi.stubGlobal('fetch', vi.fn<FetchSpy>().mockResolvedValue({ ok: false, status: 401, json: () => Promise.resolve() }))
    await vehicleApi.list().catch(() => {})
    clearUnauthorizedState()
    await vehicleApi.list().catch(() => {})
    expect(handler).toHaveBeenCalledTimes(2)
  })

  it('does not call a removed handler', async () => {
    const handler = vi.fn<() => void>()
    setUnauthorizedHandler(handler)
    setUnauthorizedHandler(null)
    vi.stubGlobal('fetch', vi.fn<FetchSpy>().mockResolvedValue({ ok: false, status: 401, json: () => Promise.resolve() }))
    await vehicleApi.list().catch(() => {})
    expect(handler).not.toHaveBeenCalled()
  })
})

describe('vehicleApi', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('list() resolves with the parsed JSON body', async () => {
    const vehicles = [{ id: 1, vin: 'ABC123' }]
    vi.stubGlobal('fetch', vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, vehicles)))
    expect(await vehicleApi.list()).toEqual(vehicles)
  })

  it('status() returns undefined on 204 No Content', async () => {
    vi.stubGlobal('fetch', vi.fn<FetchSpy>().mockResolvedValue(makeResponse(204)))
    expect(await vehicleApi.status('VIN1')).toBeUndefined()
  })

  it('throws on a non-200 error status', async () => {
    vi.stubGlobal('fetch', vi.fn<FetchSpy>().mockResolvedValue(makeResponse(500)))
    await expect(vehicleApi.list()).rejects.toThrow('API error 500')
  })

  it('history() appends from and to as query params', async () => {
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, []))
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
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, []))
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
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200))
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
