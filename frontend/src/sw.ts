/// <reference lib="webworker" />
import {
  cleanupOutdatedCaches,
  createHandlerBoundToURL,
  precacheAndRoute,
} from 'workbox-precaching'
import { NavigationRoute, registerRoute } from 'workbox-routing'

declare const self: ServiceWorkerGlobalScope

cleanupOutdatedCaches()
precacheAndRoute(self.__WB_MANIFEST)

// Every page is the same app, so opening any of them (a shortcut to /map, a reload on the trip
// log) gets the copy of index.html this worker holds, as / already does through the precache.
// Without a signal the app still opens, and shows the car as this browser last loaded it. The API
// and the live-update hub are the server's and always go to the network.
registerRoute(
  new NavigationRoute(createHandlerBoundToURL('/index.html'), {
    denylist: [/^\/api\//, /^\/hubs\//],
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
