import type { AppNotification } from '@/services/notificationsApi'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { notificationCategoryId } from '@/utils/notificationCategories'

// The same tag the service worker gives a push, so a newer notice of a kind replaces the last.
const FALLBACK_TAG = 'garagestack-notification'

async function hasPushSubscription(): Promise<boolean> {
  if (!('serviceWorker' in navigator) || !('PushManager' in window)) return false
  const registration = await navigator.serviceWorker.getRegistration()
  return (await registration?.pushManager.getSubscription()) != null
}

/**
 * Shows the Worker's notifications (engine started, door left open, ...) as system notifications
 * while the app is open in a browser without a push subscription. A subscribed browser already
 * gets each one from its service worker, so showing it here as well would show it twice. Types
 * switched off in the notification settings stay quiet, as they do in the panel.
 */
export function useBrowserNotifications() {
  const settings = useUiSettingsStore()

  async function show(notification: AppNotification): Promise<void> {
    if (!('Notification' in window) || Notification.permission !== 'granted') return

    const category = notificationCategoryId(notification.category)
    if (category !== null && settings.notificationTypeExclusions.includes(category)) return
    if (await hasPushSubscription()) return

    try {
      new Notification(notification.title, {
        body: notification.body,
        icon: '/pwa-192x192.png',
        tag: notification.category ?? FALLBACK_TAG,
      })
    } catch {
      // Some platforms only allow notifications from a service worker.
    }
  }

  return { show }
}
