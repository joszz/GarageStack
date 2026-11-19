/// <reference lib="webworker" />
import {
  cleanupOutdatedCaches,
  createHandlerBoundToURL,
  precacheAndRoute,
} from 'workbox-precaching'
import { NavigationRoute, registerRoute } from 'workbox-routing'
import { CacheFirst, NetworkFirst, StaleWhileRevalidate } from 'workbox-strategies'
import { ExpirationPlugin } from 'workbox-expiration'
import {
  MAP_TILE_CACHE,
  MAP_TILE_CACHE_MAX_AGE_SECONDS,
  MAP_TILE_CACHE_MAX_ENTRIES,
  RASTER_TILE_ORIGIN,
} from './utils/rasterTiles'
import {
  isSavedApiPath,
  SAVED_API_CACHE,
  SAVED_API_MAX_AGE_SECONDS,
  SAVED_API_MAX_ENTRIES,
} from './utils/savedApi'
import { savedApiPlugin } from './utils/savedApiPlugin'
import { BASEMAP_STYLE_URLS } from './utils/basemapStyleUrls'
import {
  BASEMAP_CACHE,
  BASEMAP_CACHE_MAX_AGE_SECONDS,
  BASEMAP_CACHE_MAX_ENTRIES,
  basemapOrigins,
  isMapRendererPath,
  MAP_RENDERER_CACHE,
  MAP_RENDERER_MAX_ENTRIES,
} from './utils/vectorTiles'

declare const self: ServiceWorkerGlobalScope

cleanupOutdatedCaches()
precacheAndRoute(self.__WB_MANIFEST)

// Every page is the same app, so opening any of them (a shortcut to /map, a reload on the trip
// log) gets the copy of index.html this worker holds, as / already does through the precache.
// Without a signal the app still opens, and shows the car as this browser last loaded it. An API
// address or the live-update hub opened as a page is the server's and goes to the network.
registerRoute(
  new NavigationRoute(createHandlerBoundToURL('/index.html'), {
    denylist: [/^\/api\//, /^\/hubs\//],
  }),
)

// The API's answers the pages read (the car, its statistics, trips, charge sessions, maintenance),
// kept so a page still shows what this browser last loaded when the server cannot be reached.
// Always the server's answer when there is one: the saved copy only stands in when the request
// fails or a proxy says the server is down, and it carries a stamp the page reads to say so.
// Commands and every other write are never kept, nor any page outside isSavedApiPath.
registerRoute(
  ({ url }) => url.origin === self.location.origin && isSavedApiPath(url.pathname),
  new NetworkFirst({
    cacheName: SAVED_API_CACHE,
    plugins: [
      new ExpirationPlugin({
        maxEntries: SAVED_API_MAX_ENTRIES,
        maxAgeSeconds: SAVED_API_MAX_AGE_SECONDS,
        purgeOnQuotaError: true,
      }),
      savedApiPlugin(),
    ],
  }),
)

// Raster map tiles this browser has shown (the dashboard's map of the parked car, and the map
// page without WebGL), kept so the card still shows the streets around the car without a signal.
// A kept tile is shown at once and asked for again in the background, so it stays current while
// online. Only tiles that were on screen: OpenStreetMap's tile policy forbids fetching areas
// ahead.
registerRoute(
  ({ url, request }) => url.origin === RASTER_TILE_ORIGIN && request.destination === 'image',
  new StaleWhileRevalidate({
    cacheName: MAP_TILE_CACHE,
    plugins: [
      // Only a tile that arrived whole, over CORS. The strategy would keep an opaque answer too
      // (status 0, which a page from before tiles asked for CORS still gets), and Chrome counts each
      // of those as megabytes of storage.
      { cacheWillUpdate: async ({ response }) => (response.status === 200 ? response : null) },
      new ExpirationPlugin({
        maxEntries: MAP_TILE_CACHE_MAX_ENTRIES,
        maxAgeSeconds: MAP_TILE_CACHE_MAX_AGE_SECONDS,
        purgeOnQuotaError: true,
      }),
    ],
  }),
)

// The vector basemap the map page and the trip routes have shown (style, tiles, fonts, sprites),
// kept the same way, so a route still has its streets without a signal. Only what was on screen.
const basemapHosts = basemapOrigins(Object.values(BASEMAP_STYLE_URLS), self.location.origin)
registerRoute(
  ({ url }) => basemapHosts.has(url.origin),
  new StaleWhileRevalidate({
    cacheName: BASEMAP_CACHE,
    plugins: [
      { cacheWillUpdate: async ({ response }) => (response.status === 200 ? response : null) },
      new ExpirationPlugin({
        maxEntries: BASEMAP_CACHE_MAX_ENTRIES,
        maxAgeSeconds: BASEMAP_CACHE_MAX_AGE_SECONDS,
        purgeOnQuotaError: true,
      }),
    ],
  }),
)

// The map renderer, once a map page has loaded it: without it the basemap above cannot be drawn
// offline. Its files are named by their content, so the kept copy is used as is.
registerRoute(
  ({ url }) => url.origin === self.location.origin && isMapRendererPath(url.pathname),
  new CacheFirst({
    cacheName: MAP_RENDERER_CACHE,
    plugins: [
      { cacheWillUpdate: async ({ response }) => (response.status === 200 ? response : null) },
      new ExpirationPlugin({ maxEntries: MAP_RENDERER_MAX_ENTRIES, purgeOnQuotaError: true }),
    ],
  }),
)

// Activate the new SW immediately instead of waiting for all tabs to close
self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', (event) => {
  event.waitUntil(
    Promise.all([
      self.clients.claim(),
      // A worker from before July 2026 switched navigation preload on, and that switch belongs to
      // the registration, so it outlived the worker: browsers that ran it still fetch every page
      // a second time for a preload nothing reads, and log that they cancelled it. Nothing here
      // uses preloadResponse, so it is switched off again.
      self.registration.navigationPreload?.disable(),
    ]),
  )
})

type BadgingNavigator = WorkerNavigator & {
  setAppBadge?: (count?: number) => Promise<void>
  clearAppBadge?: () => Promise<void>
}

/** A button on a notification, as the Worker's PushPayload describes it. */
interface PushAction {
  /** What it does: one of the names carryOut knows. */
  name: string
  title: string
  /** What the notification says once it was done. */
  sent: string
}

/** What a notification keeps for its click: where it leads, and what its buttons act on. */
interface NotificationData {
  url: string
  vin?: string
  maintenanceItemId?: number
  actions?: PushAction[]
}

self.addEventListener('push', (event) => {
  if (!event.data) return

  let payload: {
    title?: string
    body?: string
    url?: string
    category?: string
    unreadCount?: number
    vin?: string
    maintenanceItemId?: number
    actions?: PushAction[]
  } = {}
  try {
    payload = event.data.json() as typeof payload
  } catch {
    payload = { body: event.data.text() }
  }

  const title = payload.title ?? 'GarageStack'
  const tag = payload.category ?? 'garagestack-notification'
  const actions = Array.isArray(payload.actions) ? payload.actions : []
  const data: NotificationData = { url: payload.url ?? '/' }
  if (actions.length > 0) {
    data.vin = payload.vin
    data.maintenanceItemId = payload.maintenanceItemId
    data.actions = actions
  }
  // renotify (alert again for a tag that is already showing) and actions (buttons, which Safari on
  // iOS does not show) are in the Notifications spec and Chromium, but not in TypeScript's lib.
  const options: NotificationOptions & {
    renotify?: boolean
    actions?: { action: string; title: string }[]
  } = {
    body: payload.body ?? '',
    icon: '/pwa-192x192.png',
    badge: '/badge-96x96.png',
    tag,
    renotify: true,
    data,
  }
  if (actions.length > 0)
    options.actions = actions.map((action) => ({ action: action.name, title: action.title }))

  const badgeNav = self.navigator as BadgingNavigator
  const badgePromise =
    payload.unreadCount !== undefined
      ? (badgeNav.setAppBadge?.(payload.unreadCount) ?? Promise.resolve())
      : (badgeNav.setAppBadge?.() ?? Promise.resolve())

  event.waitUntil(
    Promise.all([self.registration.showNotification(title, options), badgePromise]).then(() =>
      self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
        for (const client of clientList) {
          client.postMessage({ type: 'NOTIFICATION_RECEIVED', category: payload.category })
        }
      }),
    ),
  )
})

/** A page of GarageStack itself: a push payload may never send a click to another site. */
function sameOriginUrl(rawUrl: string): string {
  try {
    const parsed = new URL(rawUrl, self.location.origin)
    return parsed.origin === self.location.origin ? parsed.href : '/'
  } catch {
    return rawUrl.startsWith('/') ? rawUrl : '/'
  }
}

function openApp(rawUrl: string): Promise<unknown> {
  const targetUrl = sameOriginUrl(rawUrl)
  return self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
    const existing = clientList.find((c) => c.url.includes(self.location.origin))
    if (existing) {
      existing.focus()
      return existing.navigate(targetUrl)
    }
    return self.clients.openWindow(targetUrl)
  })
}

// The worker's requests carry the session cookie like the page's: it is the same origin.
async function postJson(path: string, body: unknown): Promise<boolean> {
  const response = await fetch(path, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  return response.ok
}

/** Today in the device's own time zone, as the maintenance page logs a service: YYYY-MM-DD. */
function localDate(now: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

/**
 * Carries out a notification's button through the API, as the app's own buttons do. False when it
 * could not be done (no connection, signed out, the car busy with another command): the app then
 * opens, where it can be done by hand and the reason shows.
 */
async function carryOut(action: PushAction, data: NotificationData): Promise<boolean> {
  if (!data.vin) return false
  const car = `/api/vehicles/${encodeURIComponent(data.vin)}`
  try {
    switch (action.name) {
      case 'lock':
        return await postJson(`${car}/commands/lock`, { value: 'True' })
      case 'maintenance-done': {
        if (data.maintenanceItemId == null) return false
        // The odometer as the car reports it now, as the maintenance page fills it in.
        const status = await fetch(`${car}/status`, { credentials: 'same-origin' })
        if (!status.ok) return false
        const { odometerKm } = (await status.json()) as { odometerKm?: number | null }
        return await postJson(`${car}/maintenance/${data.maintenanceItemId}/log`, {
          performedAt: localDate(new Date()),
          odometerKm: odometerKm ?? null,
          notes: null,
        })
      }
      default:
        return false
    }
  } catch {
    return false
  }
}

self.addEventListener('notificationclick', (event) => {
  event.notification.close()

  const data = (event.notification.data ?? {}) as Partial<NotificationData>
  const rawUrl = data.url ?? '/'
  const action = event.action ? data.actions?.find((a) => a.name === event.action) : undefined

  // A button: done in the background, and the notification comes back saying so, quietly.
  if (action) {
    const { title, tag } = event.notification
    event.waitUntil(
      carryOut(action, { ...data, url: rawUrl }).then((done) =>
        done
          ? self.registration.showNotification(title, {
              body: action.sent,
              icon: '/pwa-192x192.png',
              badge: '/badge-96x96.png',
              tag,
              silent: true,
              data: { url: rawUrl },
            })
          : openApp(rawUrl),
      ),
    )
    return
  }

  // Clear the badge; the app will re-sync the correct count after it loads
  const badgeNav = self.navigator as BadgingNavigator
  const clearPromise = badgeNav.clearAppBadge?.() ?? Promise.resolve()

  event.waitUntil(Promise.all([clearPromise, openApp(rawUrl)]))
})
