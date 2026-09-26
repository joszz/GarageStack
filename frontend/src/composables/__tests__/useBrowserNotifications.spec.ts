import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useBrowserNotifications } from '@/composables/useBrowserNotifications'
import { useUiSettingsStore } from '@/stores/settingsUi'
import type { AppNotification } from '@/services/notificationsApi'

const shown: { title: string; options?: NotificationOptions }[] = []

function stubNotification(permission: NotificationPermission) {
  vi.stubGlobal(
    'Notification',
    class {
      static permission = permission

      constructor(title: string, options?: NotificationOptions) {
        shown.push({ title, options })
      }
    },
  )
}

function stubPushSubscription(subscribed: boolean) {
  vi.stubGlobal('PushManager', class PushManager {})
  Object.defineProperty(navigator, 'serviceWorker', {
    configurable: true,
    value: {
      getRegistration: () =>
        Promise.resolve({
          pushManager: { getSubscription: () => Promise.resolve(subscribed ? {} : null) },
        }),
    },
  })
}

const doorLeftOpen: AppNotification = {
  id: 1,
  title: 'Door left open',
  body: 'The driver door is open',
  createdAt: '2026-09-26T10:00:00Z',
  isArchived: false,
  category: 'doors-open-parked',
}

beforeEach(() => {
  setActivePinia(createPinia())
  localStorage.clear()
  shown.length = 0
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('useBrowserNotifications', () => {
  it('shows the notification in a browser without a push subscription', async () => {
    stubNotification('granted')
    stubPushSubscription(false)

    await useBrowserNotifications().show(doorLeftOpen)

    expect(shown).toEqual([
      {
        title: 'Door left open',
        options: expect.objectContaining({
          body: 'The driver door is open',
          tag: 'doors-open-parked',
        }),
      },
    ])
  })

  it('leaves it to the service worker when this browser is subscribed to push', async () => {
    stubNotification('granted')
    stubPushSubscription(true)

    await useBrowserNotifications().show(doorLeftOpen)

    expect(shown).toEqual([])
  })

  it('stays quiet for a type switched off in the notification settings', async () => {
    stubNotification('granted')
    stubPushSubscription(false)
    useUiSettingsStore().notificationTypeExclusions = ['doors-open-parked']

    await useBrowserNotifications().show(doorLeftOpen)

    expect(shown).toEqual([])
  })

  it('stays quiet until the user has allowed notifications', async () => {
    stubNotification('default')
    stubPushSubscription(false)

    await useBrowserNotifications().show(doorLeftOpen)

    expect(shown).toEqual([])
  })
})
