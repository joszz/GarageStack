import { pickSavedStandIn, SAVED_AT_HEADER, supersededBy, type SavedEntry } from './savedApi'

/** What a proxy answers when the server behind it is down: no answer, so the saved one is shown. */
const SERVER_DOWN = new Set([502, 503, 504])

/**
 * The service worker's handling of saved API answers, as a Workbox plugin for its network-first
 * route: the server's answer whenever there is one, the saved copy when there is none.
 */
export function savedApiPlugin(now: () => number = () => Date.now()) {
  // The saved answers for the same path as `url`, with when each arrived. Other paths are skipped
  // without opening them.
  async function savedForPath(cache: Cache, url: string): Promise<SavedEntry[]> {
    const { pathname } = new URL(url)
    const keys = (await cache.keys()).filter((key) => new URL(key.url).pathname === pathname)
    const entries = await Promise.all(
      keys.map(async (key) => {
        const savedAt = Date.parse((await cache.match(key))?.headers.get(SAVED_AT_HEADER) ?? '')
        return Number.isNaN(savedAt) ? null : { url: key.url, savedAt }
      }),
    )
    return entries.filter((entry) => entry !== null)
  }

  return {
    fetchDidSucceed: async ({ response }: { response: Response }): Promise<Response> => {
      if (SERVER_DOWN.has(response.status)) {
        throw new Error(`The server is unavailable (${response.status})`)
      }
      return response
    },

    // Only a whole answer is kept, stamped with when it arrived. The cookie the server may have
    // refreshed belongs to that moment and is not kept with it.
    cacheWillUpdate: async ({ response }: { response: Response }): Promise<Response | null> => {
      if (response.status !== 200) return null
      const headers = new Headers(response.headers)
      headers.set(SAVED_AT_HEADER, new Date(now()).toISOString())
      headers.delete('Set-Cookie')
      return new Response(response.body, {
        status: response.status,
        statusText: response.statusText,
        headers,
      })
    },

    // A newly saved period replaces the same period saved on an earlier visit.
    cacheDidUpdate: async ({
      cacheName,
      request,
    }: {
      cacheName: string
      request: Request
    }): Promise<void> => {
      if (!new URL(request.url).searchParams.has('from')) return
      const cache = await caches.open(cacheName)
      const saved = await savedForPath(cache, request.url)
      const savedAt = saved.find((entry) => entry.url === request.url)?.savedAt ?? now()
      await Promise.all(supersededBy(request.url, savedAt, saved).map((url) => cache.delete(url)))
    },

    // No copy of this exact address: the same period from an earlier day stands in, so "the last
    // 30 days" still shows the day after it was loaded.
    cachedResponseWillBeUsed: async ({
      cacheName,
      request,
      cachedResponse,
    }: {
      cacheName: string
      request: Request
      cachedResponse?: Response
    }): Promise<Response | undefined> => {
      if (cachedResponse) return cachedResponse
      const cache = await caches.open(cacheName)
      const standIn = pickSavedStandIn(request.url, now(), await savedForPath(cache, request.url))
      return standIn === null ? undefined : await cache.match(standIn)
    },
  }
}
