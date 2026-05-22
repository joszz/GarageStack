import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

const refreshMock = vi.fn()
const logoutMock = vi.fn()

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    accessToken: 'test-token',
    refresh: refreshMock,
    logout: logoutMock,
  }),
}))

vi.mock('@/router', () => ({
  default: { replace: vi.fn() },
}))

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
    setActivePinia(createPinia())
    refreshMock.mockReset().mockResolvedValue(true)
    logoutMock.mockReset().mockResolvedValue(undefined)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('list() resolves with the parsed JSON body', async () => {
    const vehicles = [{ id: 1, vin: 'ABC123' }]
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(makeResponse(200, vehicles)))
    expect(await vehicleApi.list()).toEqual(vehicles)
  })

  it('list() sends credentials and an Authorization header', async () => {
    const fetchSpy = vi.fn().mockResolvedValue(makeResponse(200, []))
    vi.stubGlobal('fetch', fetchSpy)
    await vehicleApi.list()
    const [, options] = fetchSpy.mock.calls[0]
    expect(options.credentials).toBe('include')
    expect(options.headers).toMatchObject({ Authorization: 'Bearer test-token' })
  })

  it('status() returns undefined on 204 No Content', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(makeResponse(204)))
    expect(await vehicleApi.status('VIN1')).toBeUndefined()
  })

  it('retries once with a refreshed token after a 401', async () => {
    const fetchSpy = vi.fn()
      .mockResolvedValueOnce(makeResponse(401))
      .mockResolvedValueOnce(makeResponse(200, []))
    vi.stubGlobal('fetch', fetchSpy)
    const result = await vehicleApi.list()
    expect(fetchSpy).toHaveBeenCalledTimes(2)
    expect(refreshMock).toHaveBeenCalledOnce()
    expect(result).toEqual([])
  })

  it('throws after 401 when token refresh fails', async () => {
    refreshMock.mockResolvedValue(false)
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(makeResponse(401)))
    await expect(vehicleApi.list()).rejects.toThrow('Session expired')
    expect(logoutMock).toHaveBeenCalledOnce()
  })

  it('throws on a non-401 error status', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(makeResponse(500)))
    await expect(vehicleApi.list()).rejects.toThrow('API error 500')
  })

  it('history() appends from and to as query params', async () => {
    const fetchSpy = vi.fn().mockResolvedValue(makeResponse(200, []))
    vi.stubGlobal('fetch', fetchSpy)
    await vehicleApi.history('VIN1', '2024-01-01', '2024-01-31')
    const [url] = fetchSpy.mock.calls[0]
    expect(url).toContain('from=2024-01-01')
    expect(url).toContain('to=2024-01-31')
  })

  it('history() omits query params when not provided', async () => {
    const fetchSpy = vi.fn().mockResolvedValue(makeResponse(200, []))
    vi.stubGlobal('fetch', fetchSpy)
    await vehicleApi.history('VIN1')
    const [url] = fetchSpy.mock.calls[0]
    expect(url).not.toContain('from=')
    expect(url).not.toContain('to=')
  })

  it('sendCommand() posts the value as a JSON body', async () => {
    const fetchSpy = vi.fn().mockResolvedValue(makeResponse(200))
    vi.stubGlobal('fetch', fetchSpy)
    await vehicleApi.sendCommand('VIN1', 'lock', 'lock')
    const [url, options] = fetchSpy.mock.calls[0]
    expect(url).toContain('/api/vehicles/VIN1/commands/lock')
    expect(options.method).toBe('POST')
    expect(JSON.parse(options.body)).toEqual({ value: 'lock' })
  })
})
