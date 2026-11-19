import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SAVED_AT_HEADER } from '../savedApi'
import { savedApiPlugin } from '../savedApiPlugin'

const CAR = 'https://garage.example/api/vehicles/FAKEVN00000000001'
const NOW = Date.parse('2026-11-18T12:00:00Z')
const DAY = 24 * 60 * 60 * 1000
const CACHE = 'garagestack-api'

// A Cache Storage holding one cache, enough for what the plugin does with it.
function fakeCache() {
  const entries = new Map<string, Response>()
  const cache = {
    keys: async () => [...entries.keys()].map((url) => new Request(url)),
    match: async (key: Request | string) =>
      entries.get(typeof key === 'string' ? key : key.url)?.clone(),
    delete: vi.fn<(key: string) => Promise<boolean>>(async (key) => entries.delete(key)),
  }
  vi.stubGlobal('caches', { open: async () => cache })
  return {
    cache,
    save(url: string, savedAt: number, body = '[]') {
      entries.set(
        url,
        new Response(body, { headers: { [SAVED_AT_HEADER]: new Date(savedAt).toISOString() } }),
      )
    },
    urls: () => [...entries.keys()],
  }
}

const trips = (from: number) => `${CAR}/trips?from=${new Date(from).toISOString()}&format=compact`

describe('savedApiPlugin', () => {
  const plugin = savedApiPlugin(() => NOW)
  let storage: ReturnType<typeof fakeCache>

  beforeEach(() => {
    storage = fakeCache()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  describe('fetchDidSucceed', () => {
    it.each([502, 503, 504])('takes a proxy saying %i as no answer at all', async (status) => {
      await expect(
        plugin.fetchDidSucceed({ response: new Response(null, { status }) }),
      ).rejects.toThrow('The server is unavailable')
    })

    it.each([200, 401, 404, 500])('passes the server answering %i on', async (status) => {
      const response = new Response(null, { status })

      await expect(plugin.fetchDidSucceed({ response })).resolves.toBe(response)
    })
  })

  describe('cacheWillUpdate', () => {
    it('keeps a whole answer stamped with when it arrived, without its cookie', async () => {
      const response = new Response('{"odometerKm":1}', {
        status: 200,
        headers: { 'Content-Type': 'application/json', 'Set-Cookie': 'session=abc' },
      })

      const kept = await plugin.cacheWillUpdate({ response })

      expect(kept!.headers.get(SAVED_AT_HEADER)).toBe(new Date(NOW).toISOString())
      expect(kept!.headers.get('Content-Type')).toBe('application/json')
      expect(kept!.headers.has('Set-Cookie')).toBe(false)
      expect(await kept!.json()).toEqual({ odometerKm: 1 })
    })

    it.each([204, 401, 404, 500])('keeps nothing of an answer with %i', async (status) => {
      const response = new Response(status === 204 ? null : '{}', { status })

      expect(await plugin.cacheWillUpdate({ response })).toBeNull()
    })
  })

  describe('cacheDidUpdate', () => {
    it('drops the same period saved on an earlier visit', async () => {
      const earlier = trips(NOW - DAY - 7 * DAY)
      const month = trips(NOW - 30 * DAY)
      const latest = trips(NOW - 7 * DAY)
      storage.save(earlier, NOW - DAY)
      storage.save(month, NOW - 60_000)
      storage.save(latest, NOW)

      await plugin.cacheDidUpdate({ cacheName: CACHE, request: new Request(latest) })

      expect(storage.urls().sort()).toEqual([latest, month].sort())
    })

    it('leaves the cache alone for an answer without a period', async () => {
      storage.save(`${CAR}/status`, NOW)

      await plugin.cacheDidUpdate({ cacheName: CACHE, request: new Request(`${CAR}/status`) })

      expect(storage.cache.delete).not.toHaveBeenCalled()
    })
  })

  describe('cachedResponseWillBeUsed', () => {
    it('uses the copy of the exact address when there is one', async () => {
      const exact = new Response('exact')

      const used = await plugin.cachedResponseWillBeUsed({
        cacheName: CACHE,
        request: new Request(`${CAR}/status`),
        cachedResponse: exact,
      })

      expect(used).toBe(exact)
    })

    it('stands in the same period from an earlier day when the address is new', async () => {
      const yesterday = trips(NOW - DAY - 7 * DAY)
      storage.save(yesterday, NOW - DAY, '[{"id":7}]')

      const used = await plugin.cachedResponseWillBeUsed({
        cacheName: CACHE,
        request: new Request(trips(NOW - 7 * DAY)),
      })

      expect(await used!.json()).toEqual([{ id: 7 }])
    })

    it('has nothing when no saved period fits', async () => {
      storage.save(trips(NOW - 30 * DAY), NOW - DAY)

      const used = await plugin.cachedResponseWillBeUsed({
        cacheName: CACHE,
        request: new Request(trips(NOW - 7 * DAY)),
      })

      expect(used).toBeUndefined()
    })
  })
})
