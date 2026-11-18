import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'

import { createHandlerBoundToURL } from 'workbox-precaching'
import { NavigationRoute, registerRoute } from 'workbox-routing'

vi.mock('workbox-precaching', () => ({
  cleanupOutdatedCaches: vi.fn<() => void>(),
  precacheAndRoute: vi.fn<() => void>(),
  createHandlerBoundToURL: vi.fn<(url: string) => () => Promise<Response>>(
    () => () => Promise.resolve(new Response('app shell')),
  ),
}))

// The real NavigationRoute keeps its lists private; this one shows what the worker passed.
vi.mock('workbox-routing', () => ({
  registerRoute: vi.fn<(route: unknown) => void>(),
  NavigationRoute: class {
    constructor(
      readonly handler: unknown,
      readonly options: { allowlist?: RegExp[]; denylist?: RegExp[] },
    ) {}
  },
}))

// Strategies and plugins as plain records of what the worker configured them with.
vi.mock('workbox-strategies', () => ({
  StaleWhileRevalidate: class {
    constructor(readonly options: unknown) {}
  },
}))

vi.mock('workbox-expiration', () => ({
  ExpirationPlugin: class {
    constructor(readonly options: Record<string, unknown>) {}
  },
}))

type Listener = (event: unknown) => void

interface FakeClient {
  url: string
  postMessage: ReturnType<typeof vi.fn<(message: unknown) => void>>
  focus: ReturnType<typeof vi.fn<() => void>>
  navigate: ReturnType<typeof vi.fn<(url: string) => Promise<null>>>
}

const ORIGIN = 'https://garage.example'

// The parts of ServiceWorkerGlobalScope sw.ts touches, recording what it does with them.
function fakeWorkerScope(clients: FakeClient[] = []) {
  const listeners = new Map<string, Listener>()
  return {
    listeners,
    __WB_MANIFEST: [],
    location: { origin: ORIGIN },
    addEventListener: (type: string, listener: Listener) => listeners.set(type, listener),
    skipWaiting: vi.fn<() => Promise<void>>(),
    registration: {
      showNotification: vi
        .fn<(title: string, options: NotificationOptions) => Promise<void>>()
        .mockResolvedValue(),
      navigationPreload: { disable: vi.fn<() => Promise<void>>().mockResolvedValue() },
    },
    navigator: {
      setAppBadge: vi.fn<(count?: number) => Promise<void>>().mockResolvedValue(),
      clearAppBadge: vi.fn<() => Promise<void>>().mockResolvedValue(),
    },
    clients: {
      matchAll: vi.fn<() => Promise<FakeClient[]>>().mockResolvedValue(clients),
      openWindow: vi.fn<(url: string) => Promise<null>>().mockResolvedValue(null),
      claim: vi.fn<() => Promise<void>>().mockResolvedValue(),
    },
  }
}

function client(url = `${ORIGIN}/map`): FakeClient {
  return {
    url,
    postMessage: vi.fn<(message: unknown) => void>(),
    focus: vi.fn<() => void>(),
    navigate: vi.fn<(url: string) => Promise<null>>().mockResolvedValue(null),
  }
}

// Fires an event at the worker's listener and waits for what it handed to waitUntil.
async function dispatch(scope: ReturnType<typeof fakeWorkerScope>, type: string, event: object) {
  let pending: Promise<unknown> = Promise.resolve()
  scope.listeners.get(type)!({ ...event, waitUntil: (p: Promise<unknown>) => (pending = p) })
  await pending
}

function pushEvent(body: unknown) {
  const text = typeof body === 'string' ? body : JSON.stringify(body)
  return { data: { text: () => text, json: () => JSON.parse(text) } }
}

async function loadWorker(clients: FakeClient[] = []) {
  const scope = fakeWorkerScope(clients)
  vi.stubGlobal('self', scope)
  vi.resetModules()
  await import('../sw')
  return scope
}

describe('service worker', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.mocked(registerRoute).mockClear()
    vi.mocked(createHandlerBoundToURL).mockClear()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows a pushed notification, sets the badge and tells the open windows', async () => {
    const window = client()
    const scope = await loadWorker([window])

    await dispatch(
      scope,
      'push',
      pushEvent({
        title: 'Door left open',
        body: 'Front left',
        url: '/?card=doors',
        category: 'doors-open-parked',
        unreadCount: 3,
      }),
    )

    expect(scope.registration.showNotification).toHaveBeenCalledWith(
      'Door left open',
      expect.objectContaining({
        body: 'Front left',
        tag: 'doors-open-parked',
        data: { url: '/?card=doors' },
      }),
    )
    expect(scope.navigator.setAppBadge).toHaveBeenCalledWith(3)
    expect(window.postMessage).toHaveBeenCalledWith({
      type: 'NOTIFICATION_RECEIVED',
      category: 'doors-open-parked',
    })
  })

  it('shows a plain-text push under the app name', async () => {
    const scope = await loadWorker()

    await dispatch(scope, 'push', pushEvent('Charging complete'))

    expect(scope.registration.showNotification).toHaveBeenCalledWith(
      'GarageStack',
      expect.objectContaining({ body: 'Charging complete', data: { url: '/' } }),
    )
  })

  it('ignores a push without a payload', async () => {
    const scope = await loadWorker()

    scope.listeners.get('push')!({
      data: null,
      waitUntil: vi.fn<(promise: Promise<unknown>) => void>(),
    })

    expect(scope.registration.showNotification).not.toHaveBeenCalled()
  })

  it('opens the notification in an open window, clearing the badge', async () => {
    const window = client()
    const scope = await loadWorker([window])
    const notification = { close: vi.fn<() => void>(), data: { url: '/statistics' } }

    await dispatch(scope, 'notificationclick', { notification })

    expect(notification.close).toHaveBeenCalled()
    expect(scope.navigator.clearAppBadge).toHaveBeenCalled()
    expect(window.focus).toHaveBeenCalled()
    expect(window.navigate).toHaveBeenCalledWith(`${ORIGIN}/statistics`)
  })

  it('opens a new window when none is open', async () => {
    const scope = await loadWorker()

    await dispatch(scope, 'notificationclick', {
      notification: { close: vi.fn<() => void>(), data: { url: '/map' } },
    })

    expect(scope.clients.openWindow).toHaveBeenCalledWith(`${ORIGIN}/map`)
  })

  // A push payload names where a click goes; it may only ever be a page of GarageStack itself.
  it('never sends a click to another site', async () => {
    const scope = await loadWorker()

    await dispatch(scope, 'notificationclick', {
      notification: { close: vi.fn<() => void>(), data: { url: 'https://evil.example/phish' } },
    })

    expect(scope.clients.openWindow).toHaveBeenCalledWith('/')
  })

  describe('buttons on a notification', () => {
    const VIN = 'FAKEVN00000000001'
    const lock = { name: 'lock', title: 'Lock', sent: 'Lock sent to the car.' }
    const done = { name: 'maintenance-done', title: 'Mark as done', sent: 'Logged as done today.' }

    function shown(scope: ReturnType<typeof fakeWorkerScope>, call = 0) {
      return scope.registration.showNotification.mock.calls[call] as unknown as [
        string,
        NotificationOptions & { actions?: unknown; data: Record<string, unknown> },
      ]
    }

    function clickEvent(action: string, data: Record<string, unknown>) {
      return {
        action,
        notification: {
          close: vi.fn<() => void>(),
          title: 'Car left unlocked',
          tag: 'unlocked-parked',
          data: { url: '/', vin: VIN, ...data },
        },
      }
    }

    function stubFetch(...responses: Response[]) {
      const fetch = vi.fn<(input: string, init?: RequestInit) => Promise<Response>>()
      for (const response of responses) fetch.mockResolvedValueOnce(response)
      vi.stubGlobal('fetch', fetch)
      return fetch
    }

    it('shows the buttons the push names, and keeps what they act on', async () => {
      const scope = await loadWorker()

      await dispatch(
        scope,
        'push',
        pushEvent({
          title: 'Car left unlocked',
          body: 'Parked and unlocked',
          category: 'unlocked-parked',
          vin: VIN,
          actions: [lock],
        }),
      )

      const [, options] = shown(scope)
      expect(options.actions).toEqual([{ action: 'lock', title: 'Lock' }])
      expect(options.data).toEqual({
        url: '/',
        vin: VIN,
        maintenanceItemId: undefined,
        actions: [lock],
      })
    })

    it('shows no buttons, and keeps no car, for a push without actions', async () => {
      const scope = await loadWorker()

      await dispatch(scope, 'push', pushEvent({ title: 'Charged', vin: VIN }))

      const [, options] = shown(scope)
      expect(options.actions).toBeUndefined()
      expect(options.data).toEqual({ url: '/' })
    })

    it('locks the car without opening the app, and says so', async () => {
      const fetch = stubFetch(new Response(null, { status: 202 }))
      const scope = await loadWorker()

      await dispatch(scope, 'notificationclick', clickEvent('lock', { actions: [lock] }))

      expect(fetch).toHaveBeenCalledExactlyOnceWith(`/api/vehicles/${VIN}/commands/lock`, {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ value: 'True' }),
      })
      const [title, options] = shown(scope)
      expect(title).toBe('Car left unlocked')
      expect(options).toMatchObject({
        body: 'Lock sent to the car.',
        tag: 'unlocked-parked',
        silent: true,
      })
      expect(scope.clients.openWindow).not.toHaveBeenCalled()
    })

    it.each([
      ['the server refuses', () => stubFetch(new Response(null, { status: 409 }))],
      [
        'there is no connection',
        () =>
          vi.stubGlobal(
            'fetch',
            vi.fn<() => Promise<Response>>().mockRejectedValue(new TypeError('Failed to fetch')),
          ),
      ],
    ])('opens the app when %s', async (_, stub) => {
      stub()
      const scope = await loadWorker()

      await dispatch(scope, 'notificationclick', clickEvent('lock', { actions: [lock] }))

      expect(scope.registration.showNotification).not.toHaveBeenCalled()
      expect(scope.clients.openWindow).toHaveBeenCalledWith(`${ORIGIN}/`)
    })

    it('logs the maintenance item as done today, at the odometer the car reports now', async () => {
      vi.useFakeTimers({ now: new Date(2026, 10, 18, 23, 30), toFake: ['Date'] })
      const fetch = stubFetch(
        Response.json({ odometerKm: 48123.4 }),
        Response.json({ item: {}, entry: {} }),
      )
      const scope = await loadWorker()

      await dispatch(
        scope,
        'notificationclick',
        clickEvent('maintenance-done', { maintenanceItemId: 12, actions: [done] }),
      )
      vi.useRealTimers()

      expect(fetch.mock.calls[0]![0]).toBe(`/api/vehicles/${VIN}/status`)
      const [path, init] = fetch.mock.calls[1]!
      expect(path).toBe(`/api/vehicles/${VIN}/maintenance/12/log`)
      expect(JSON.parse(init!.body as string)).toEqual({
        performedAt: '2026-11-18',
        odometerKm: 48123.4,
        notes: null,
      })
      expect(shown(scope)[1].body).toBe('Logged as done today.')
    })

    it('logs nothing when the odometer cannot be read, and opens the app', async () => {
      const fetch = stubFetch(new Response(null, { status: 401 }))
      const scope = await loadWorker()

      await dispatch(
        scope,
        'notificationclick',
        clickEvent('maintenance-done', { maintenanceItemId: 12, actions: [done] }),
      )

      expect(fetch).toHaveBeenCalledOnce()
      expect(scope.clients.openWindow).toHaveBeenCalled()
    })

    it('treats a button the push did not name as a plain click', async () => {
      const fetch = stubFetch()
      const scope = await loadWorker()

      await dispatch(scope, 'notificationclick', clickEvent('unlock', { actions: [lock] }))

      expect(fetch).not.toHaveBeenCalled()
      expect(scope.navigator.clearAppBadge).toHaveBeenCalled()
      expect(scope.clients.openWindow).toHaveBeenCalledWith(`${ORIGIN}/`)
    })
  })

  describe('opening a page', () => {
    async function navigationRoute() {
      await loadWorker()
      const route = vi
        .mocked(registerRoute)
        .mock.calls.map((call) => call[0])
        .find((r) => r instanceof NavigationRoute)
      expect(route).toBeDefined()
      return route as unknown as NavigationRoute & {
        handler: unknown
        options: { denylist: RegExp[] }
      }
    }

    it("serves every page from the worker's copy of the app, so it opens without a signal", async () => {
      const route = await navigationRoute()

      expect(createHandlerBoundToURL).toHaveBeenCalledWith('/index.html')
      expect(route.handler).toBe(vi.mocked(createHandlerBoundToURL).mock.results[0]!.value)
    })

    it.each(['/api/auth/oidc/login', '/api/reference', '/hubs/telemetry'])(
      'leaves %s to the server',
      async (path) => {
        const route = await navigationRoute()
        expect(route.options.denylist.some((pattern) => pattern.test(path))).toBe(true)
      },
    )

    it.each(['/', '/map', '/trip-log', '/login', '/apiary'])(
      'takes %s, a page of the app',
      async (path) => {
        const route = await navigationRoute()
        expect(route.options.denylist.some((pattern) => pattern.test(path))).toBe(false)
      },
    )
  })

  describe('map tiles', () => {
    type Matcher = (o: { url: URL; request: { destination: string } }) => boolean
    interface Plugin {
      cacheWillUpdate?: (p: { response: Response }) => Promise<Response | null>
      options?: Record<string, unknown>
    }
    interface Strategy {
      options: { cacheName: string; plugins: Plugin[] }
    }

    async function tileRoute() {
      await loadWorker()
      const call = vi.mocked(registerRoute).mock.calls.find((c) => typeof c[0] === 'function')
      expect(call).toBeDefined()
      return { matches: call![0] as unknown as Matcher, strategy: call![1] as unknown as Strategy }
    }

    const at = (href: string, destination = 'image') => ({
      url: new URL(href),
      request: { destination },
    })

    it('keeps the raster tiles the page shows', async () => {
      const { matches, strategy } = await tileRoute()

      expect(matches(at('https://tile.openstreetmap.org/14/8414/5394.png'))).toBe(true)
      expect(strategy.options.cacheName).toBe('garagestack-map-tiles')
    })

    it.each([
      ['a vector tile', 'https://tiles.openfreemap.org/planet/14/8414/5394.pbf', 'image'],
      ['the API', 'https://garage.example/api/vehicles', ''],
      ['a page on the tile host', 'https://tile.openstreetmap.org/', 'document'],
    ])('leaves %s alone', async (_, href, destination) => {
      const { matches } = await tileRoute()

      expect(matches(at(href, destination))).toBe(false)
    })

    it('keeps a whole tile but not an opaque or failed answer', async () => {
      const { strategy } = await tileRoute()
      const keep = strategy.options.plugins.find((p) => p.cacheWillUpdate)!.cacheWillUpdate!
      const tile = new Response('png', { status: 200 })

      expect(await keep({ response: tile })).toBe(tile)
      expect(await keep({ response: new Response(null, { status: 404 }) })).toBeNull()
      expect(await keep({ response: { status: 0 } as Response })).toBeNull()
    })

    it('holds the cache to about 150 tiles and a month', async () => {
      const { strategy } = await tileRoute()
      const expiration = strategy.options.plugins.find((p) => p.options)!

      expect(expiration.options).toMatchObject({
        maxEntries: 150,
        maxAgeSeconds: 30 * 24 * 60 * 60,
        purgeOnQuotaError: true,
      })
    })
  })
})
