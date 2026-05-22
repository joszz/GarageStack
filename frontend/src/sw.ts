/// <reference lib="webworker" />
import { cleanupOutdatedCaches, precacheAndRoute } from 'workbox-precaching'
import { registerRoute } from 'workbox-routing'
import { NetworkFirst } from 'workbox-strategies'

declare const self: ServiceWorkerGlobalScope

cleanupOutdatedCaches()
precacheAndRoute(self.__WB_MANIFEST)

registerRoute(
  ({ url }) => url.pathname.startsWith('/api/'),
  new NetworkFirst({ cacheName: 'api-cache', networkTimeoutSeconds: 10 }),
)

self.addEventListener('push', (event) => {
  if (!event.data) return

  let payload: { title?: string; body?: string; url?: string } = {}
  try {
    payload = event.data.json() as typeof payload
  } catch {
    payload = { body: event.data.text() }
  }

  const title = payload.title ?? 'GarageStack'
  const options: NotificationOptions = {
    body: payload.body ?? '',
    icon: '/pwa-192x192.png',
    badge: '/pwa-64x64.png',
    tag: 'garagestack-notification',
    data: { url: payload.url ?? '/' },
  }

  event.waitUntil(self.registration.showNotification(title, options))
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()

  const targetUrl = (event.notification.data as { url?: string })?.url ?? '/'

  event.waitUntil(
    self.clients
      .matchAll({ type: 'window', includeUncontrolled: true })
      .then((clientList) => {
        const existing = clientList.find((c) => c.url.includes(self.location.origin))
        if (existing) {
          existing.focus()
          return existing.navigate(targetUrl)
        }
        return self.clients.openWindow(targetUrl)
      }),
  )
})
