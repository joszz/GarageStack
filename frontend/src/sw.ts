/// <reference lib="webworker" />
import { cleanupOutdatedCaches, precacheAndRoute } from 'workbox-precaching'

declare const self: ServiceWorkerGlobalScope

cleanupOutdatedCaches()
precacheAndRoute(self.__WB_MANIFEST)

// Activate the new SW immediately instead of waiting for all tabs to close
self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', (event) => {
  event.waitUntil(self.registration.navigationPreload?.enable() ?? Promise.resolve())
})

type BadgingNavigator = WorkerNavigator & {
  setAppBadge?: (count?: number) => Promise<void>
  clearAppBadge?: () => Promise<void>
}

self.addEventListener('push', (event) => {
  if (!event.data) return

  let payload: {
    title?: string
    body?: string
    url?: string
    category?: string
    unreadCount?: number
  } = {}
  try {
    payload = event.data.json() as typeof payload
  } catch {
    payload = { body: event.data.text() }
  }

  const title = payload.title ?? 'GarageStack'
  const tag = payload.category ?? 'garagestack-notification'
  const options: NotificationOptions = {
    body: payload.body ?? '',
    icon: '/pwa-192x192.png',
    badge: '/pwa-64x64.png',
    tag,
    renotify: true,
    data: { url: payload.url ?? '/' },
  }

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

self.addEventListener('notificationclick', (event) => {
  event.notification.close()

  const rawUrl = (event.notification.data as { url?: string })?.url ?? '/'
  const targetUrl = (() => {
    try {
      const parsed = new URL(rawUrl, self.location.origin)
      return parsed.origin === self.location.origin ? parsed.href : '/'
    } catch {
      return rawUrl.startsWith('/') ? rawUrl : '/'
    }
  })()

  // Clear the badge — the app will re-sync the correct count after it loads
  const badgeNav = self.navigator as BadgingNavigator
  const clearPromise = badgeNav.clearAppBadge?.() ?? Promise.resolve()

  event.waitUntil(
    Promise.all([
      clearPromise,
      self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
        const existing = clientList.find((c) => c.url.includes(self.location.origin))
        if (existing) {
          existing.focus()
          return existing.navigate(targetUrl)
        }
        return self.clients.openWindow(targetUrl)
      }),
    ]),
  )
})
