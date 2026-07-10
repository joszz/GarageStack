import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'

import { maintenanceApi } from '@/services/maintenanceApi'

function makeResponse(status: number, body?: unknown) {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: () => Promise.resolve(body),
  }
}

type FetchSpy = (...args: Parameters<typeof fetch>) => Promise<ReturnType<typeof makeResponse>>

describe('maintenanceApi', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('list() resolves with the parsed JSON body', async () => {
    const items = [{ id: 1, name: 'Oil change' }]
    vi.stubGlobal('fetch', vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, items)))
    expect(await maintenanceApi.list('VIN1')).toEqual(items)
  })

  it('list() calls the correct vehicle-scoped URL', async () => {
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, []))
    vi.stubGlobal('fetch', fetchSpy)
    await maintenanceApi.list('VIN1')
    const [url] = fetchSpy.mock.calls[0]!
    expect(url).toBe('/api/vehicles/VIN1/maintenance')
  })

  it('create() posts a JSON body and returns the parsed response', async () => {
    const created = { id: 1, name: 'Oil change' }
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, created))
    vi.stubGlobal('fetch', fetchSpy)
    const result = await maintenanceApi.create('VIN1', { name: 'Oil change', intervalKm: 10000 })
    const [url, options] = fetchSpy.mock.calls[0]!
    expect(url).toBe('/api/vehicles/VIN1/maintenance')
    expect(options!.method).toBe('POST')
    expect(JSON.parse(options!.body as string)).toEqual({ name: 'Oil change', intervalKm: 10000 })
    expect(result).toEqual(created)
  })

  it('update() puts a JSON body to the item URL', async () => {
    const updated = { id: 1, name: 'Oil change (updated)' }
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, updated))
    vi.stubGlobal('fetch', fetchSpy)
    const result = await maintenanceApi.update('VIN1', 1, { name: 'Oil change (updated)' })
    const [url, options] = fetchSpy.mock.calls[0]!
    expect(url).toBe('/api/vehicles/VIN1/maintenance/1')
    expect(options!.method).toBe('PUT')
    expect(result).toEqual(updated)
  })

  it('delete() sends a DELETE request', async () => {
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200))
    vi.stubGlobal('fetch', fetchSpy)
    await maintenanceApi.delete('VIN1', 1)
    const [url, options] = fetchSpy.mock.calls[0]!
    expect(url).toBe('/api/vehicles/VIN1/maintenance/1')
    expect(options!.method).toBe('DELETE')
  })

  it('listLog() resolves with the parsed JSON body', async () => {
    const entries = [{ id: 1, performedAt: '2026-01-01' }]
    vi.stubGlobal('fetch', vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, entries)))
    expect(await maintenanceApi.listLog('VIN1', 1)).toEqual(entries)
  })

  it('logService() posts and returns the item + logEntry envelope', async () => {
    const response = { item: { id: 1 }, logEntry: { id: 5 } }
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, response))
    vi.stubGlobal('fetch', fetchSpy)
    const result = await maintenanceApi.logService('VIN1', 1, { performedAt: '2026-07-10' })
    const [url, options] = fetchSpy.mock.calls[0]!
    expect(url).toBe('/api/vehicles/VIN1/maintenance/1/log')
    expect(options!.method).toBe('POST')
    expect(result).toEqual(response)
  })

  it('deleteLogEntry() sends a DELETE request to the log entry URL', async () => {
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200))
    vi.stubGlobal('fetch', fetchSpy)
    await maintenanceApi.deleteLogEntry('VIN1', 1, 5)
    const [url, options] = fetchSpy.mock.calls[0]!
    expect(url).toBe('/api/vehicles/VIN1/maintenance/1/log/5')
    expect(options!.method).toBe('DELETE')
  })

  it('throws on a non-200 error status', async () => {
    vi.stubGlobal('fetch', vi.fn<FetchSpy>().mockResolvedValue(makeResponse(500)))
    await expect(maintenanceApi.list('VIN1')).rejects.toThrow('API error 500')
  })
})
