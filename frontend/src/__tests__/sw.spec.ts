import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'

vi.mock('workbox-precaching', () => ({
  cleanupOutdatedCaches: vi.fn<() => void>(),
  precacheAndRoute: vi.fn<() => void>(),
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
})
