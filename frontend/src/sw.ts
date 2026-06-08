/// <reference lib="webworker" />
import { cleanupOutdatedCaches, precacheAndRoute } from 'workbox-precaching'

declare const self: ServiceWorkerGlobalScope

cleanupOutdatedCaches()
precacheAndRoute(self.__WB_MANIFEST)

// Activate the new SW immediately instead of waiting for all tabs to close
self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()))

self.addEventListener('push', (event) => {
  if (!event.data) return

  let payload: { title?: string; body?: string; url?: string; category?: string } = {}
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

  event.waitUntil(
    self.registration.showNotification(title, options).then(() =>
      self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
        for (const client of clientList) {
          client.postMessage({ type: 'NOTIFICATION_RECEIVED' })
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

  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
      const existing = clientList.find((c) => c.url.includes(self.location.origin))
      if (existing) {
        existing.focus()
        return existing.navigate(targetUrl)
      }
      return self.clients.openWindow(targetUrl)
    }),
  )
})
